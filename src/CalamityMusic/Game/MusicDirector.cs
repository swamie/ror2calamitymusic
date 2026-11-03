using System;
using System.Collections.Generic;
using CalamityMusic.Audio;
using CalamityMusic.Library;
using CalamityMusic.UI;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CalamityMusic.Game
{
    /// <summary>
    /// Watches the game and decides what plays:
    ///   teleporter turns red -> buildup -> (boss spawns) -> drop into that boss's Calamity theme
    ///   other boss fights (Mithrix, Voidling, False Son, Aurelionite, modded) -> straight drop
    ///   fight over -> crossfade back to the stage's Calamity area theme (or the game's own music)
    ///   no fight -> the stage/menu plays a Calamity area theme picked from CalamityMusic.StageThemes.cfg
    /// Everything it reads (teleporter state, boss group members) is networked, so it runs locally
    /// on every player's game with no host-side setup.
    /// </summary>
    internal sealed class MusicDirector : MonoBehaviour
    {
        private enum State { Idle, Buildup, Playing, Ending }
        private enum Kind { None, Teleporter, Encounter, Escape, Preview }

        private struct Sighting
        {
            public CharacterBody Body;
            public BossTheme Theme;
            public float Health;
            public bool FromTeleporter;
        }

        private MusicEngine engine;
        private MusicLibrary library;
        private BossThemes themes;
        private StageThemes stageThemes;
        private NowPlayingBanner banner;

        private State state = State.Idle;
        private Kind kind = Kind.None;
        private float stateStart;
        private BossTheme theme;
        private BossTheme pendingTheme;
        private TrackInfo currentTrack;
        private TeleporterInteraction encounterTp;
        private TeleporterInteraction handledTp;
        private float buildupLength;
        private float lastBossSeen;
        private bool bossSeen;
        private float previewEnd;
        private int failedLoads;
        private bool warnedThisStage;
        private bool runOver;
        private bool usingFileBuildup;
        private bool holdStarted;
        private float retryAfter;

        // Stage ("ambient") music: plays while state is Idle.
        private bool ambientActive;
        private string ambientScene;
        private BossTheme ambientTheme;
        private TrackInfo ambientTrack;
        private float ambientRetryAfter;

        private readonly List<Sighting> alive = new List<Sighting>();
        private Sighting? dominant;
        private float scanTimer;
        private float volumeTimer;
        private float gameVolume = 1f;
        private bool focusOnly;

        private EscapeSequenceController escape;
        private float escapeSearchTimer;
        private BuffIndex empyreanBuff = BuffIndex.None;
        private bool empyreanLookedUp;
        private readonly System.Random rng = new System.Random();

        public void Init(MusicEngine engine, MusicLibrary library, BossThemes themes, StageThemes stageThemes, NowPlayingBanner banner)
        {
            this.engine = engine;
            this.library = library;
            this.themes = themes;
            this.stageThemes = stageThemes;
            this.banner = banner;
            SceneManager.activeSceneChanged += OnSceneChanged;
            Run.onClientGameOverGlobal += OnGameOver;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Run.onClientGameOverGlobal -= OnGameOver;
        }

        private void OnGameOver(Run run, RunReport report)
        {
            // Let the game-over screen have its own music; nothing new starts until the next stage/run.
            runOver = true;
            if (state != State.Idle && kind != Kind.Preview) End(Time.unscaledTime);
            else if (ambientActive) StopAmbient(2f);
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            // Stage music keeps playing through the loading screen; the next frame decides whether the
            // new scene wants a different theme and crossfades if so.
            if (state == State.Idle && ambientActive) ambientRetryAfter = 0f;
            else if (kind != Kind.Preview || Run.instance) Stop(0.6f);
            handledTp = null;
            encounterTp = null;
            escape = null;
            warnedThisStage = false;
            runOver = false;
            retryAfter = 0f;
            failedLoads = 0;
            BossGroupTracker.Prune();
            library.RescanAsync();
        }

        // ------------------------------------------------------------------ frame loop

        private int frameErrors;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            try
            {
                Frame(Time.unscaledTime, dt);
            }
            catch (Exception e)
            {
                if (frameErrors++ < 10) Log.Error("Music director error: " + e);
            }
            finally
            {
                // Always runs, so the game's music can never get stuck muted by an error above.
                VanillaMusic.Update(dt);
            }
        }

        private void Frame(float now, float dt)
        {
            if (library.PollInstallMarker(dt))
                banner.ShowHint("New songs installed. They're ready for the next fight.");
            UpdateEngineSettings(dt);
            HandleEngineEvents(now);

            if (ModConfig.Mode.Value == MusicMode.Vanilla || !engine.Available)
            {
                if (state != State.Idle) Stop(0.8f);
                else if (ambientActive) StopAmbient(0.8f);
                return;
            }

            if (!Run.instance && kind != Kind.Preview && state != State.Idle) Stop(1.5f);
            if (runOver && kind != Kind.Preview) return;

            scanTimer -= dt;
            if (scanTimer <= 0f)
            {
                scanTimer = 0.2f;
                ScanBosses(now);
            }

            if (Run.instance) WatchTeleporter(now);
            Tick(now);
            if (state == State.Idle) UpdateAmbient(now);
        }

        private void UpdateEngineSettings(float dt)
        {
            volumeTimer -= dt;
            if (volumeTimer <= 0f)
            {
                volumeTimer = 0.4f;
                gameVolume = ModConfig.FollowGameVolume.Value ? VanillaMusic.MasterVolume01 * VanillaMusic.SettingVolume01 : 1f;
                var focus = AudioManager.AudioFocusedOnlyConVar.instance;
                focusOnly = focus != null && focus.onlyPlayWhenFocused;
            }

            float volume = Mathf.Clamp01(ModConfig.Volume.Value / 100f) * gameVolume;
            if (state == State.Idle && ambientActive) volume *= Mathf.Clamp01(ModConfig.StageVolume.Value / 100f);
            if (focusOnly && !Application.isFocused) volume = 0f;
            engine.Volume = volume;
            engine.BassBoostDb = ModConfig.BassBoost.Value;
            engine.BassFrequency = ModConfig.BassFrequency.Value;

            bool paused = false;
            if (ModConfig.MuffleWhenPaused.Value)
            {
                try { paused = PauseManager.isPaused || PauseManager.PauseScreenIsOpen(); }
                catch { paused = PauseManager.isPaused; }
            }
            // Risk of Options is opened from the pause menu, so never muffle the Preview button.
            engine.Muffle = paused && kind != Kind.Preview ? 1f : 0f;
        }

        // ------------------------------------------------------------------ boss detection

        private void ScanBosses(float now)
        {
            alive.Clear();
            dominant = null;
            if (!Run.instance) return;
            BossGroupTracker.RefreshIfUnpatched(now);

            if (!empyreanLookedUp)
            {
                empyreanLookedUp = true;
                try { empyreanBuff = BuffCatalog.FindBuffIndex("bdEmpyrean"); }
                catch { empyreanBuff = BuffIndex.None; }
            }

            var tp = TeleporterInteraction.instance;
            var groups = BossGroupTracker.Groups;
            for (int g = groups.Count - 1; g >= 0; g--)
            {
                var group = groups[g];
                if (!group)
                {
                    groups.RemoveAt(g);
                    continue;
                }
                var squad = group.combatSquad;
                if (!squad) continue;
                bool fromTp = tp && group == tp.bossGroup;

                var members = squad.readOnlyMembersList;
                for (int m = 0; m < members.Count; m++)
                {
                    var master = members[m];
                    if (!master) continue;
                    var body = master.GetBody();
                    if (!body) continue;
                    var hc = body.healthComponent;
                    if (!hc || !hc.alive) continue;
                    if (body.teamComponent && body.teamComponent.teamIndex == TeamIndex.Player) continue;

                    var s = new Sighting { Body = body, Theme = ThemeFor(body), Health = hc.fullCombinedHealth, FromTeleporter = fromTp };
                    alive.Add(s);
                    if (dominant == null
                        || s.Theme.Priority > dominant.Value.Theme.Priority
                        || (s.Theme.Priority == dominant.Value.Theme.Priority && s.Health > dominant.Value.Health))
                        dominant = s;
                }
            }
        }

        private BossTheme ThemeFor(CharacterBody body)
        {
            if (empyreanBuff != BuffIndex.None && body.HasBuff(empyreanBuff))
                return themes.Custom("Empyrean", themes.EmpyreanSlots.Value, 40);

            string bodyName = BodyCatalog.GetBodyName(body.bodyIndex);
            if (string.IsNullOrEmpty(bodyName)) bodyName = body.name.Replace("(Clone)", "").Trim();
            string display = null;
            try { display = body.GetDisplayName(); } catch { /* language not ready */ }
            return themes.Resolve(bodyName, display);
        }

        private bool BossAlive => alive.Count > 0;
        private bool TeleporterBossAlive => alive.Exists(s => s.FromTeleporter);

        // ------------------------------------------------------------------ state machine

        private void WatchTeleporter(float now)
        {
            var tp = TeleporterInteraction.instance;
            if (!tp || tp == handledTp) return;

            if (tp.isIdleToCharging || tp.isCharging)
            {
                handledTp = tp;
                bool freshActivation = tp.isIdleToCharging || (tp.chargeFraction < 0.03f && !TeleporterBossAlive);
                if ((state == State.Idle || state == State.Ending) && freshActivation)
                {
                    BeginTeleporter(tp, now);
                }
                else if (state == State.Playing && kind == Kind.Encounter)
                {
                    // Already fighting something (e.g. a Nemesis invasion); keep the song but stay until the teleporter is done.
                    kind = Kind.Teleporter;
                    encounterTp = tp;
                }
            }
            else if (tp.isCharged || tp.isInFinalSequence)
            {
                handledTp = tp; // joined after it was charged
            }
        }

        private void Tick(float now)
        {
            switch (state)
            {
                case State.Idle:
                    if (dominant != null && Run.instance && now >= retryAfter)
                    {
                        var d = dominant.Value;
                        var tp = TeleporterInteraction.instance;
                        bool tpFight = d.FromTeleporter || (tp && tp == handledTp && tp.isCharging);
                        StartEncounter(d.Theme, tpFight ? Kind.Teleporter : Kind.Encounter, now,
                            Transition.Drop(ImpactStrength, 0.03f));
                        if (tpFight) encounterTp = tp;
                    }
                    break;

                case State.Buildup:
                {
                    if (kind == Kind.Teleporter && !encounterTp)
                    {
                        Stop(0.5f);
                        break;
                    }
                    if (kind != Kind.Preview && dominant != null) pendingTheme = dominant.Value.Theme;
                    if (pendingTheme != null && pendingTheme.KeepVanilla)
                    {
                        Stop(0.6f);
                        break;
                    }

                    float t = now - stateStart;
                    if (t >= buildupLength && pendingTheme != null) Drop(pendingTheme, now);
                    else if (t >= buildupLength + 8f) Drop(themes.Generic(), now); // the boss never turned up
                    else if (t >= buildupLength && usingFileBuildup && !holdStarted)
                    {
                        // The custom buildup ran out before the boss showed up: hold the tension with the synth.
                        holdStarted = true;
                        engine.StartBuildup(new BuildupSynth(buildupLength, rng.Next(), true));
                    }
                    break;
                }

                case State.Playing:
                    if (BossAlive)
                    {
                        lastBossSeen = now;
                        bossSeen = true;
                    }

                    if (kind == Kind.Teleporter || kind == Kind.Encounter)
                    {
                        if (dominant != null && theme != null)
                        {
                            var d = dominant.Value.Theme;
                            if (d.Priority > theme.Priority && !d.SharesSlotWith(theme) && !d.KeepVanilla)
                                SwitchTheme(d, now);
                        }
                        if (theme != null && theme.IsFinale && EscapeActive())
                        {
                            BeginEscape(now);
                            break;
                        }
                    }
                    if (ShouldEnd(now)) End(now);
                    break;

                case State.Ending:
                    if (dominant != null && kind != Kind.Preview && Run.instance)
                    {
                        var d = dominant.Value;
                        StartEncounter(d.Theme, d.FromTeleporter ? Kind.Teleporter : Kind.Encounter, now, Transition.Drop(ImpactStrength, 0.03f));
                        break;
                    }
                    if (now - stateStart > ModConfig.FadeOutSeconds.Value + 0.3f)
                    {
                        state = State.Idle;
                        kind = Kind.None;
                        theme = null;
                    }
                    break;
            }
        }

        private bool ShouldEnd(float now)
        {
            switch (kind)
            {
                case Kind.Preview:
                    return now >= previewEnd;
                case Kind.Escape:
                    return !EscapeActive();
                case Kind.Teleporter:
                {
                    bool bossesGone = !BossAlive && now - lastBossSeen > 2.5f;
                    if (!encounterTp) return bossesGone;
                    bool tpDone = encounterTp.isCharged || encounterTp.isInFinalSequence
                                  || encounterTp.activationState >= TeleporterInteraction.ActivationState.Charged;
                    if (ModConfig.EndsWhen.Value == MusicEndsWhen.BossDefeated && bossSeen) return bossesGone;
                    return tpDone && bossesGone;
                }
                default:
                    return !BossAlive && now - lastBossSeen > (theme != null ? theme.LingerSeconds : 4f);
            }
        }

        private static float ImpactStrength => Mathf.Clamp01(ModConfig.ImpactStrength.Value / 100f);
        private static bool LoopSongs => ModConfig.OnSongEnd.Value == SongEndBehaviour.LoopSong;

        /// <summary>
        /// Starts a song. Parts of a multi-part song don't loop: the next part follows when one ends,
        /// and only the last part loops.
        /// </summary>
        private void PlayTrack(TrackInfo track, bool loop, Transition transition)
        {
            bool hasNextPart = library.NextInSequence(track) != null;
            engine.Play(track, loop && !hasNextPart, transition);
            currentTrack = track;
        }

        private TrackInfo PickFor(BossTheme t, TrackInfo avoid)
        {
            var track = library.Pick(t.Slots, ModConfig.Mode.Value, avoid, ModConfig.AnySongFallback.Value, out bool fallback);
            if (track != null && fallback)
                Log.Info($"No songs for [{string.Join(", ", t.Slots)}], using {track.Slot}.");
            return track;
        }

        private bool LibraryEmpty()
        {
            if (library.TrackCount > 0) return false;
            if (!warnedThisStage)
            {
                warnedThisStage = true;
                banner.ShowHint("No Calamity songs found. Settings > Mod Options > Calamity Music > Open Music Folder");
                Log.Warning("No songs found in " + library.Root + ". See HOW_TO_ADD_SONGS.txt in that folder.");
            }
            return true;
        }

        private void BeginTeleporter(TeleporterInteraction tp, float now)
        {
            encounterTp = tp;
            if (!ModConfig.BuildupEnabled.Value || LibraryEmpty()) return; // the Idle state drops in when the boss appears

            kind = Kind.Teleporter;
            pendingTheme = null;
            bossSeen = false;
            StartBuildup(now);
        }

        private void StartBuildup(float now)
        {
            buildupLength = Mathf.Clamp(ModConfig.BuildupLength.Value, 1.5f, 15f);
            holdStarted = false;

            var style = ModConfig.Buildup.Value;
            string file = style == BuildupStyle.Synth ? null : library.PickBuildup();
            usingFileBuildup = file != null && (style == BuildupStyle.CustomFiles || rng.Next(2) == 0);
            if (usingFileBuildup) engine.StartBuildupFile(file); // length arrives with BuildupReady
            else engine.StartBuildup(new BuildupSynth(buildupLength, rng.Next()));

            VanillaMusic.FadeOut();
            ambientActive = false; // the buildup fades the stage music out
            state = State.Buildup;
            stateStart = now;
            lastBossSeen = now;
        }

        private void Drop(BossTheme t, float now)
        {
            var track = PickFor(t, null);
            if (track == null)
            {
                LibraryEmpty();
                Stop(0.5f);
                return;
            }
            failedLoads = 0;
            PlayTrack(track, LoopSongs, Transition.Drop(ImpactStrength));
            theme = t;
            currentTrack = track;
            state = State.Playing;
            stateStart = now;
            lastBossSeen = now;
            Log.Info($"Drop: {t.Key} -> {track}");
        }

        private void StartEncounter(BossTheme t, Kind k, float now, Transition transition)
        {
            if (t.KeepVanilla || LibraryEmpty()) return;
            var track = PickFor(t, null);
            if (track == null)
            {
                retryAfter = now + 5f; // nothing to play for this boss; don't re-search every frame
                return;
            }

            failedLoads = 0;
            VanillaMusic.FadeOut();
            ambientActive = false;
            PlayTrack(track, LoopSongs, transition);
            state = State.Playing;
            kind = k;
            theme = t;
            currentTrack = track;
            stateStart = now;
            lastBossSeen = now;
            bossSeen = BossAlive;
            Log.Info($"{k}: {t.Key} -> {track}");
        }

        private void SwitchTheme(BossTheme t, float now)
        {
            var track = PickFor(t, currentTrack);
            theme = t;
            if (track == null || (currentTrack != null && track.Path == currentTrack.Path)) return;
            PlayTrack(track, LoopSongs, Transition.Crossfade(2.5f));
            Log.Info($"Switching theme: {t.Key} -> {track}");
        }

        private void BeginEscape(float now)
        {
            var track = ModConfig.MoonEscapeMusic.Value ? library.PickEscape(ModConfig.Mode.Value) : null;
            if (track == null)
            {
                if (ModConfig.StageMusic.Value)
                {
                    kind = Kind.Escape; // no escape song: keep the fight song going until we're out
                    return;
                }
                End(now); // let the vanilla escape music take over
                return;
            }
            kind = Kind.Escape;
            theme = null;
            PlayTrack(track, true, Transition.Crossfade(2f));
            Log.Info("Moon escape -> " + track);
        }

        private bool EscapeActive()
        {
            if (!escape)
            {
                escapeSearchTimer -= Time.unscaledDeltaTime;
                if (escapeSearchTimer > 0f) return false;
                escapeSearchTimer = 3f;
                escape = FindObjectOfType<EscapeSequenceController>();
                if (!escape) return false;
            }
            var sm = escape.mainStateMachine;
            return sm && sm.state is EscapeSequenceController.EscapeSequenceMainState;
        }

        private void End(float now)
        {
            if (StartAmbient(now, Transition.Crossfade(Mathf.Max(0.5f, ModConfig.FadeOutSeconds.Value))))
            {
                // The boss theme crossfades straight into the stage's area theme.
                state = State.Idle;
                kind = Kind.None;
                theme = null;
                pendingTheme = null;
                return;
            }
            engine.StopAll(Mathf.Max(0.2f, ModConfig.FadeOutSeconds.Value));
            VanillaMusic.FadeIn();
            state = State.Ending;
            stateStart = now;
        }

        private void Stop(float fade)
        {
            engine.StopAll(fade);
            VanillaMusic.FadeIn();
            ambientActive = false;
            ambientRetryAfter = 0f;
            state = State.Idle;
            kind = Kind.None;
            theme = null;
            pendingTheme = null;
            currentTrack = null;
        }

        // ------------------------------------------------------------------ stage music

        private SceneDef cachedSceneDef;
        private string cachedSceneName, cachedSceneDisplay;
        private float ambientTimer;

        private string CurrentScene(out string displayName)
        {
            var def = SceneCatalog.mostRecentSceneDef;
            if (!def)
            {
                displayName = null;
                return SceneManager.GetActiveScene().name;
            }
            if (def != cachedSceneDef)
            {
                cachedSceneDef = def;
                cachedSceneName = string.IsNullOrEmpty(def.cachedName) ? def.baseSceneName : def.cachedName;
                cachedSceneDisplay = null;
                try { cachedSceneDisplay = Language.GetString(def.nameToken); } catch { /* language not loaded */ }
            }
            displayName = cachedSceneDisplay;
            return cachedSceneName;
        }

        /// <summary>Whether this scene should play a Calamity area theme right now, and which.</summary>
        private bool StageMusicWanted(out BossTheme stage)
        {
            stage = null;
            if (!ModConfig.StageMusic.Value || runOver || !library.HasStageMusic) return false;
            if (!Run.instance && !ModConfig.MenuMusic.Value) return false;
            // A boss the player mapped to Vanilla keeps the game's boss music.
            if (dominant != null && dominant.Value.Theme.KeepVanilla) return false;
            string scene = CurrentScene(out string display);
            if (string.IsNullOrEmpty(scene)) return false;
            stage = stageThemes.Resolve(scene, display);
            return stage != null && !stage.KeepVanilla;
        }

        private void UpdateAmbient(float now)
        {
            ambientTimer -= Time.unscaledDeltaTime;
            if (ambientTimer > 0f) return;
            ambientTimer = 0.2f;

            if (!StageMusicWanted(out var stage))
            {
                if (ambientActive) StopAmbient(1.5f);
                else VanillaMusic.FadeIn();
                return;
            }

            bool playing = engine.ActiveMusicVoice != 0;
            if (ambientActive && playing && (stage.Key == ambientScene || stage.SharesSlotWith(ambientTheme)))
            {
                // Same theme (e.g. menu -> character select): keep the song going.
                ambientScene = stage.Key;
                ambientTheme = stage;
                return;
            }
            if (now < ambientRetryAfter) return;
            StartAmbient(now, Transition.Crossfade(ambientActive && playing ? 2.5f : 2f));
        }

        private bool StartAmbient(float now, Transition transition)
        {
            if (!StageMusicWanted(out var stage)) return false;
            var track = library.PickStage(stage.Slots, ModConfig.Mode.Value, ambientTrack);
            if (track == null)
            {
                ambientRetryAfter = now + 10f;
                return false;
            }
            PlayTrack(track, LoopSongs, transition);
            VanillaMusic.FadeOut();
            ambientActive = true;
            ambientScene = stage.Key;
            ambientTheme = stage;
            ambientTrack = track;
            ambientRetryAfter = now + 4f; // give it time to load before trying again
            Log.Info($"Stage music: {stage.Key} -> {track}");
            return true;
        }

        private void StopAmbient(float fade)
        {
            ambientActive = false;
            engine.StopAll(fade);
            VanillaMusic.FadeIn();
        }

        // ------------------------------------------------------------------ engine feedback

        private void HandleEngineEvents(float now)
        {
            while (engine.Events.TryDequeue(out var e))
            {
                switch (e.Kind)
                {
                    case EngineEventKind.TrackStarted:
                        if (e.Message == "loop" || state == State.Ending || e.Track == null) break;
                        if (state == State.Idle && !(ambientActive && ModConfig.ShowStageNowPlaying.Value)) break;
                        // Later parts of a multi-part song carry straight on; the banner already showed the title.
                        if (ModConfig.ShowNowPlaying.Value && !library.IsContinuation(e.Track)) banner.Show(e.Track);
                        break;

                    case EngineEventKind.TrackEnding:
                    {
                        if (state == State.Idle && ambientActive && e.VoiceId == engine.ActiveMusicVoice)
                        {
                            var nextStagePart = library.NextInSequence(e.Track);
                            if (nextStagePart != null)
                                PlayTrack(nextStagePart, LoopSongs, Transition.Append());
                            else if (!LoopSongs && ambientTheme != null)
                            {
                                var other = library.PickStage(ambientTheme.Slots, ModConfig.Mode.Value, e.Track);
                                if (other != null)
                                {
                                    PlayTrack(other, false, Transition.Crossfade(3f));
                                    ambientTrack = other;
                                }
                            }
                            break;
                        }
                        if (state != State.Playing || e.VoiceId != engine.ActiveMusicVoice) break;
                        var nextPart = library.NextInSequence(e.Track);
                        if (nextPart != null)
                            PlayTrack(nextPart, kind == Kind.Escape || LoopSongs, Transition.Append());
                        else if (!LoopSongs)
                            PlayNext(Transition.Crossfade(3f));
                        break;
                    }

                    case EngineEventKind.TrackEnded:
                        if (state == State.Playing && engine.ActiveMusicVoice == 0) PlayNext(Transition.FadeIn(0.3f));
                        break;

                    case EngineEventKind.BuildupReady:
                        if (state == State.Buildup && usingFileBuildup && e.Seconds > 0.5)
                            buildupLength = Mathf.Clamp((float)e.Seconds - 0.05f, 1.5f, 20f);
                        break;

                    case EngineEventKind.BuildupFailed:
                        Log.Warning("Could not play buildup " + e.Message);
                        if (state == State.Buildup && usingFileBuildup)
                        {
                            usingFileBuildup = false;
                            engine.StartBuildup(new BuildupSynth(Mathf.Max(1.5f, buildupLength - (now - stateStart)), rng.Next()));
                            buildupLength = Mathf.Max(now - stateStart + 1.5f, buildupLength);
                        }
                        break;

                    case EngineEventKind.LoadFailed:
                        if (e.Track == null)
                        {
                            // A buildup file broke mid-way; the hold synth covers it if the boss is late.
                            Log.Warning("Buildup playback stopped: " + e.Message);
                            break;
                        }
                        Log.Warning($"Could not play '{e.Track.Path}': {e.Message}");
                        library.MarkBad(e.Track);
                        if (state == State.Idle && ambientActive) ambientRetryAfter = now + 1f; // pick another stage song
                        if (state == State.Playing && failedLoads++ < 3)
                            PlayNext(Transition.Drop(0f, 0.02f));
                        else if (state == State.Playing)
                            End(now);
                        break;
                }
            }
        }

        private void PlayNext(Transition transition)
        {
            TrackInfo next = kind == Kind.Escape
                ? library.PickEscape(ModConfig.Mode.Value)
                : theme != null ? PickFor(theme, currentTrack) : null;
            if (next == null) return;
            PlayTrack(next, LoopSongs || kind == Kind.Escape, transition);
        }

        // ------------------------------------------------------------------ settings menu buttons

        /// <summary>Plays a buildup and drop into a random theme so volume/bass can be tuned from the menu.</summary>
        public void TogglePreview()
        {
            float now = Time.unscaledTime;
            if (kind == Kind.Preview && state != State.Idle)
            {
                Stop(1f);
                return;
            }
            if (ModConfig.Mode.Value == MusicMode.Vanilla)
            {
                banner.ShowHint("Music is set to Vanilla. Pick Calamity, Infernum or Mixed first.");
                return;
            }
            warnedThisStage = false;
            if (LibraryEmpty()) return;

            string slot = library.RandomFilledSlot(ModConfig.Mode.Value);
            Stop(0.2f);
            kind = Kind.Preview;
            pendingTheme = themes.Custom("Preview", slot ?? Slots.Generic, 1);
            StartBuildup(now);
            previewEnd = now + buildupLength + 30f;
        }

        public void ShowTestBanner()
        {
            banner.ShowSample();
        }
    }
}
