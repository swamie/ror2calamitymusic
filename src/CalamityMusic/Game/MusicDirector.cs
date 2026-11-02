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
    ///   fight over -> fade out, give the game its music back
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

        public void Init(MusicEngine engine, MusicLibrary library, BossThemes themes, NowPlayingBanner banner)
        {
            this.engine = engine;
            this.library = library;
            this.themes = themes;
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
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            if (kind != Kind.Preview || Run.instance) Stop(0.6f);
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
            UpdateEngineSettings(dt);
            HandleEngineEvents(now);

            if (ModConfig.Mode.Value == MusicMode.Vanilla || !engine.Available)
            {
                if (state != State.Idle) Stop(0.8f);
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
            engine.Play(track, LoopSongs, Transition.Drop(ImpactStrength));
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
            engine.Play(track, LoopSongs, transition);
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
            engine.Play(track, LoopSongs, Transition.Crossfade(2.5f));
            currentTrack = track;
            Log.Info($"Switching theme: {t.Key} -> {track}");
        }

        private void BeginEscape(float now)
        {
            var track = ModConfig.MoonEscapeMusic.Value ? library.PickEscape(ModConfig.Mode.Value) : null;
            if (track == null)
            {
                End(now); // let the vanilla escape music take over
                return;
            }
            kind = Kind.Escape;
            theme = null;
            currentTrack = track;
            engine.Play(track, true, Transition.Crossfade(2f));
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
            engine.StopAll(Mathf.Max(0.2f, ModConfig.FadeOutSeconds.Value));
            VanillaMusic.FadeIn();
            state = State.Ending;
            stateStart = now;
        }

        private void Stop(float fade)
        {
            engine.StopAll(fade);
            VanillaMusic.FadeIn();
            state = State.Idle;
            kind = Kind.None;
            theme = null;
            pendingTheme = null;
            currentTrack = null;
        }

        // ------------------------------------------------------------------ engine feedback

        private void HandleEngineEvents(float now)
        {
            while (engine.Events.TryDequeue(out var e))
            {
                switch (e.Kind)
                {
                    case EngineEventKind.TrackStarted:
                        if (e.Message == "loop" || state == State.Idle || state == State.Ending) break;
                        if (ModConfig.ShowNowPlaying.Value && e.Track != null) banner.Show(e.Track.Display);
                        break;

                    case EngineEventKind.TrackEnding:
                        if (state != State.Playing || LoopSongs || e.VoiceId != engine.ActiveMusicVoice) break;
                        PlayNext(Transition.Crossfade(3f));
                        break;

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
            engine.Play(next, LoopSongs || kind == Kind.Escape, transition);
            currentTrack = next;
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
            banner.Show(currentTrack != null ? currentTrack.Display : "ENNWAY! - Edenic Whispers");
        }
    }
}
