using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using CalamityMusic.Audio;
using CalamityMusic.Library;
using CalamityMusic.UI;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace CalamityMusic.Game
{
    /// <summary>
    /// Watches the game and decides what plays:
    ///   teleporter turns red -> buildup -> (boss spawns) -> drop into that boss's Calamity theme
    ///   other boss fights (Mithrix, Voidling, False Son, Aurelionite, modded) -> straight drop
    ///   multi-phase fights move through the parts of multi-part songs; the last stand hits harder
    ///   fight over -> crossfade back to the stage's Calamity area theme (or the game's own music)
    ///   no fight -> the stage/menu plays a Calamity area theme picked from CalamityMusic.StageThemes.cfg
    ///   Simulacrum -> Boss Rush, a tier every 10 waves
    /// Everything it reads (teleporter state, boss group members, encounter states) is networked,
    /// so it runs locally on every player's game; MusicSync makes clients play the host's picks.
    /// </summary>
    internal sealed class MusicDirector : MonoBehaviour
    {
        private enum State { Idle, Buildup, Playing, Ending }
        private enum Kind { None, Teleporter, Encounter, Escape, Preview, BossRush }

        private struct Sighting
        {
            public CharacterBody Body;
            public string BodyName;
            public BossTheme Theme;
            public float Health;
            public float Fraction;
            public bool FromTeleporter;
        }

        private MusicEngine engine;
        private MusicLibrary library;
        private BossThemes themes;
        private StageThemes stageThemes;
        private NowPlayingBanner banner;
        private BossTitleCard titleCard;
        private MusicSync sync;

        private State state = State.Idle;
        private Kind kind = Kind.None;
        private float stateStart;
        private BossTheme theme;
        private BossTheme pendingTheme;
        private TrackInfo currentTrack;
        private string currentContext;
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

        // Phases and the last stand.
        private bool phaseLocked;      // the song's parts follow the fight's phases instead of running on
        private int lastPhase, phaseCount;
        private bool lastStandDone;
        private float bassBonus;
        private int brotherPhase;
        private EntityStateMachine brotherEsm;
        private float brotherSearchAfter;
        private readonly HashSet<string> titled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Simulacrum Boss Rush.
        private int bossRushTier;
        private float bossRushTimer;
        private bool bossRushWanted;

        // Stage ("ambient") music: plays while state is Idle.
        private bool ambientActive;
        private string ambientScene;
        private BossTheme ambientTheme;
        private TrackInfo ambientTrack;
        private float ambientRetryAfter;

        // The local player.
        private CharacterBody lastBody;
        private bool lastAlive;
        private bool tapeStopped;
        private float deadSince;

        private readonly List<Sighting> alive = new List<Sighting>();
        private Sighting? dominant;
        private float scanTimer;
        private bool scannedThisFrame;
        private float volumeTimer;
        private float gameVolume = 1f;
        private bool focusOnly;

        private EscapeSequenceController escape;
        private float escapeSearchTimer;
        private BuffIndex empyreanBuff = BuffIndex.None;
        private bool empyreanLookedUp;
        private readonly System.Random rng = new System.Random();

        public void Init(MusicEngine engine, MusicLibrary library, BossThemes themes, StageThemes stageThemes,
            NowPlayingBanner banner, BossTitleCard titleCard, MusicSync sync)
        {
            this.engine = engine;
            this.library = library;
            this.themes = themes;
            this.stageThemes = stageThemes;
            this.banner = banner;
            this.titleCard = titleCard;
            this.sync = sync;
            SceneManager.activeSceneChanged += OnSceneChanged;
            Run.onClientGameOverGlobal += OnGameOver;
            if (sync != null) sync.HostChoiceArrived += OnHostChoice;
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Run.onClientGameOverGlobal -= OnGameOver;
            if (sync != null) sync.HostChoiceArrived -= OnHostChoice;
        }

        private void OnGameOver(Run run, RunReport report)
        {
            // Let the game-over screen have its own music; nothing new starts until the next stage/run.
            runOver = true;
            tapeStopped = false; // stopping the music below also resets the tape
            if (state != State.Idle && kind != Kind.Preview) End(Time.unscaledTime);
            else if (ambientActive) StopAmbient(2f);
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            // Stage music keeps playing through the loading screen; the next frame decides whether the
            // new scene wants a different theme and crossfades if so.
            if (state == State.Idle && ambientActive) ambientRetryAfter = 0f;
            else if (kind == Kind.BossRush && Run.instance is InfiniteTowerRun) { /* Boss Rush carries on between Simulacrum stages */ }
            else if (kind != Kind.Preview || Run.instance) Stop(0.6f);
            if (tapeStopped)
            {
                tapeStopped = false;
                engine.TapeStart(0.4f);
            }
            handledTp = null;
            encounterTp = null;
            escape = null;
            brotherEsm = null;
            brotherPhase = 0;
            brotherSearchAfter = 0f;
            titled.Clear();
            lastBody = null;
            lastAlive = false;
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
            sync?.Poll();
            UpdateEngineSettings(dt);
            HandleEngineEvents(now);

            if (ModConfig.Mode.Value == MusicMode.Vanilla || !engine.Available)
            {
                if (state != State.Idle) Stop(0.8f);
                else if (ambientActive) StopAmbient(0.8f);
                engine.LowHealth = 0f;
                if (tapeStopped)
                {
                    tapeStopped = false;
                    engine.TapeStart(0.3f);
                }
                return;
            }

            if (!Run.instance && kind != Kind.Preview && state != State.Idle) Stop(1.5f);
            UpdateLocalPlayer(now);
            HandleHotkeys(now);
            if (runOver && kind != Kind.Preview) return;

            if (Run.instance && kind != Kind.Preview && UpdateBossRush(now, dt)) return;

            scannedThisFrame = false;
            scanTimer -= dt;
            if (scanTimer <= 0f)
            {
                scanTimer = 0.2f;
                ScanBosses(now);
                scannedThisFrame = true;
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
            engine.BassBoostDb = ModConfig.BassBoost.Value + bassBonus;
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

        /// <summary>Calamity normally, Infernum on Monsoon/Eclipse (and harder modded difficulties) in Auto mode.</summary>
        private MusicMode Mode
        {
            get
            {
                var m = ModConfig.Mode.Value;
                return m != MusicMode.Auto ? m : HardDifficulty() ? MusicMode.Infernum : MusicMode.Calamity;
            }
        }

        private static bool HardDifficulty()
        {
            var run = Run.instance;
            if (!run) return false;
            try
            {
                var def = DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty);
                return def != null && (def.countsAsHardMode || def.scalingValue >= 3f);
            }
            catch
            {
                return false;
            }
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

                    string bodyName = BodyName(body);
                    var s = new Sighting
                    {
                        Body = body, BodyName = bodyName, Theme = ThemeFor(body, bodyName),
                        Health = hc.fullCombinedHealth, Fraction = hc.combinedHealthFraction, FromTeleporter = fromTp,
                    };
                    alive.Add(s);
                    if (dominant == null
                        || s.Theme.Priority > dominant.Value.Theme.Priority
                        || (s.Theme.Priority == dominant.Value.Theme.Priority && s.Health > dominant.Value.Health))
                        dominant = s;
                }
            }

            brotherPhase = BrotherPhase(now);
        }

        private static string BodyName(CharacterBody body)
        {
            string name = BodyCatalog.GetBodyName(body.bodyIndex);
            return string.IsNullOrEmpty(name) ? body.name.Replace("(Clone)", "").Trim() : name;
        }

        private BossTheme ThemeFor(CharacterBody body, string bodyName)
        {
            if (empyreanBuff != BuffIndex.None && body.HasBuff(empyreanBuff))
                return themes.Custom("Empyrean", themes.EmpyreanSlots.Value, 40);

            string display = null;
            try { display = body.GetDisplayName(); } catch { /* language not ready */ }
            return themes.Resolve(bodyName, display);
        }

        private bool BossAlive => alive.Count > 0;
        private bool TeleporterBossAlive => alive.Exists(s => s.FromTeleporter);

        /// <summary>Mithrix's encounter phase from the moon's encounter state machine (networked), or 0.</summary>
        private int BrotherPhase(float now)
        {
            if (!brotherEsm)
            {
                brotherEsm = null;
                bool mithrix = dominant != null && dominant.Value.BodyName != null
                               && dominant.Value.BodyName.StartsWith("Brother", StringComparison.OrdinalIgnoreCase);
                string scene = CurrentScene(out _);
                bool moon = scene != null && scene.StartsWith("moon", StringComparison.OrdinalIgnoreCase);
                if ((!mithrix && !moon) || now < brotherSearchAfter) return 0;
                brotherSearchAfter = now + 3f;
                foreach (var esm in FindObjectsOfType<EntityStateMachine>())
                {
                    if (esm && esm.state != null && esm.state.GetType().Namespace == "EntityStates.Missions.BrotherEncounter")
                    {
                        brotherEsm = esm;
                        break;
                    }
                }
                if (!brotherEsm) return 0;
            }
            var st = brotherEsm.state;
            return st == null ? 0 : BossPhases.FromBrotherState(st.GetType().Name);
        }

        /// <summary>The phase of the fight going on now, and how many it has, when we can tell.</summary>
        private bool BossPhase(out int phase, out int count)
        {
            phase = count = 0;
            if (!ModConfig.FollowBossPhases.Value) return false;
            if (brotherPhase > 0)
            {
                phase = brotherPhase;
                count = BossPhases.MithrixPhases;
                return true;
            }
            return dominant != null && BossPhases.FromBodyName(dominant.Value.BodyName, out phase, out count);
        }

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
                    // Mithrix's phase 2 has no Mithrix in it; the encounter still counts as the fight.
                    if (BossAlive || brotherPhase > 0)
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
                        if (scannedThisFrame)
                        {
                            FollowPhases();
                            CheckLastStand(now);
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
                case Kind.BossRush:
                    return false; // UpdateBossRush decides
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
                {
                    float linger = theme != null ? theme.LingerSeconds : 4f;
                    // Between a multi-phase boss's phases (Voidling diving away, False Son changing form) keep going.
                    if (phaseCount > 1 && lastPhase > 0 && lastPhase < phaseCount) linger = Mathf.Max(linger, 90f);
                    return !BossAlive && now - lastBossSeen > linger;
                }
            }
        }

        private static float ImpactStrength => Mathf.Clamp01(ModConfig.ImpactStrength.Value / 100f);
        private static bool LoopSongs => ModConfig.OnSongEnd.Value == SongEndBehaviour.LoopSong;

        /// <summary>
        /// Starts a song. Parts of a multi-part song don't loop: the next part follows when one ends,
        /// and only the last part loops, unless <paramref name="followSequence"/> is off (parts that
        /// follow a boss's phases, Boss Rush tiers), in which case the part loops until told otherwise.
        /// With a <paramref name="context"/> the host tells modded clients what it picked.
        /// </summary>
        private void PlayTrack(TrackInfo track, bool loop, Transition transition, bool followSequence = true, string context = null)
        {
            bool hasNextPart = followSequence && library.NextInSequence(track) != null;
            engine.Play(track, loop && !hasNextPart, transition);
            currentTrack = track;
            if (context != null)
            {
                currentContext = context;
                if (NetworkServer.active) sync?.Announce(context, library.RelPath(track));
            }
        }

        private static string BossContext(BossTheme t) => t == null ? null : "boss:" + t.Key;

        /// <summary>The host's pick for this moment when we're a client and have that file.</summary>
        private TrackInfo HostPick(string context, TrackInfo avoid = null)
        {
            if (sync == null || !sync.TryGetHostChoice(context, out string rel)) return null;
            var t = library.FindByRelPath(rel);
            if (t == null || library.IsBanned(t) || (avoid != null && t.Path == avoid.Path)) return null;
            return t;
        }

        private TrackInfo PickFor(BossTheme t, TrackInfo avoid, bool useHost = true)
        {
            var host = useHost ? HostPick(BossContext(t), avoid) : null;
            if (host != null) return host;
            var track = library.Pick(t.Slots, Mode, avoid, ModConfig.AnySongFallback.Value, out bool fallback);
            if (track != null && fallback)
                Log.Info($"No songs for [{string.Join(", ", t.Slots)}], using {track.Slot}.");
            return track;
        }

        /// <summary>
        /// For a fight with phases and a song with parts, swaps in the part that fits the current
        /// phase and locks the parts to the fight.
        /// </summary>
        private TrackInfo ApplyPhase(TrackInfo track, out bool locked)
        {
            locked = false;
            if (track == null || !BossPhase(out int p, out int count)) return track;
            lastPhase = p;
            phaseCount = count;
            int parts = library.SequenceLength(track);
            if (parts <= 1) return track;
            locked = true;
            return library.PartAt(track, BossPhases.PartForPhase(p, count, parts)) ?? track;
        }

        /// <summary>Plays a boss song, fitted to the fight's phase if it has parts.</summary>
        private void PlayBossTrack(TrackInfo track, Transition transition, string context)
        {
            var fitted = ApplyPhase(track, out bool locked);
            phaseLocked = locked;
            PlayTrack(fitted, locked || LoopSongs, transition, !locked, context);
        }

        private void ResetFight()
        {
            phaseLocked = false;
            lastPhase = phaseCount = 0;
            lastStandDone = false;
            bassBonus = 0f;
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
            ResetFight();
            PlayBossTrack(track, Transition.Drop(ImpactStrength), kind == Kind.Preview ? null : BossContext(t));
            theme = t;
            state = State.Playing;
            stateStart = now;
            lastBossSeen = now;
            if (kind != Kind.Preview) ShowTitleCard(dominant);
            Log.Info($"Drop: {t.Key} -> {currentTrack}");
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
            ResetFight();
            PlayBossTrack(track, transition, BossContext(t));
            state = State.Playing;
            kind = k;
            theme = t;
            stateStart = now;
            lastBossSeen = now;
            bossSeen = BossAlive;
            ShowTitleCard(dominant);
            Log.Info($"{k}: {t.Key} -> {currentTrack}");
        }

        private void SwitchTheme(BossTheme t, float now)
        {
            var track = PickFor(t, currentTrack);
            theme = t;
            if (track == null || (currentTrack != null && track.Path == currentTrack.Path)) return;
            ResetFight();
            PlayBossTrack(track, Transition.Crossfade(2.5f), BossContext(t));
            ShowTitleCard(dominant);
            Log.Info($"Switching theme: {t.Key} -> {currentTrack}");
        }

        /// <summary>Moves a multi-part song on to the part for the fight's current phase (never backwards).</summary>
        private void FollowPhases()
        {
            if (currentTrack == null || theme == null) return;
            if (!BossPhase(out int p, out int count)) return;
            lastPhase = p;
            phaseCount = count;
            int parts = library.SequenceLength(currentTrack);
            if (parts <= 1 || count <= 1) return;
            int target = BossPhases.PartForPhase(p, count, parts);
            if (target <= library.PartPosition(currentTrack)) return;
            var next = library.PartAt(currentTrack, target);
            if (next == null || next.Path == currentTrack.Path) return;
            phaseLocked = true;
            PlayTrack(next, true, Transition.Crossfade(1.5f), false);
            Log.Info($"Phase {p}/{count} -> part {target}: {next}");
        }

        /// <summary>
        /// The last boss standing below 25% (in its final phase, for bosses with phases): jump to the
        /// song's last part, or hit the drop impact and lift the bass if it has no parts.
        /// </summary>
        private void CheckLastStand(float now)
        {
            if (lastStandDone || !ModConfig.LastStand.Value || dominant == null || alive.Count != 1 || currentTrack == null) return;
            if (now - stateStart < 3f) return;
            var d = dominant.Value;
            if (d.Fraction <= 0f || d.Fraction > 0.25f) return;
            bool phased = BossPhase(out int p, out int count);
            if (phased && p < count) return; // not the final phase yet

            lastStandDone = true;
            int parts = library.SequenceLength(currentTrack);
            if (!phased && parts > 1 && library.PartPosition(currentTrack) < parts)
            {
                var last = library.PartAt(currentTrack, parts);
                if (last != null)
                {
                    phaseLocked = true;
                    PlayTrack(last, true, Transition.Drop(ImpactStrength * 0.8f, 0.02f), false);
                    Log.Info("Last stand -> " + last);
                    return;
                }
            }
            engine.PlayImpact(Mathf.Max(0.35f, ImpactStrength) * 0.75f);
            bassBonus = 2f;
            Log.Info("Last stand: impact and +2 dB bass");
        }

        private void ShowTitleCard(Sighting? s)
        {
            if (!ModConfig.BossTitleCards.Value || s == null || titleCard == null) return;
            var body = s.Value.Body;
            if (!body) return;
            if (!titled.Add(BossPhases.Family(s.Value.BodyName ?? body.name))) return;
            string name = null, subtitle = null;
            try
            {
                name = body.GetDisplayName();
                subtitle = body.GetSubtitle();
            }
            catch
            {
                // language not ready
            }
            if (string.IsNullOrEmpty(name)) return;
            var style = currentTrack != null ? BannerStyles.For(currentTrack) : BannerStyles.Default;
            bool infernum = currentTrack != null && currentTrack.Pack.Equals(MusicLibrary.PackInfernum, StringComparison.OrdinalIgnoreCase);
            titleCard.Show(name, subtitle, style, infernum ? 1.35f : 1f);
        }

        private void BeginEscape(float now)
        {
            var track = ModConfig.MoonEscapeMusic.Value ? HostPick("escape") ?? library.PickEscape(Mode) : null;
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
            ResetFight();
            PlayTrack(track, true, Transition.Crossfade(2f), true, "escape");
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
            ResetFight();
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
            currentContext = null;
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
            currentContext = null;
            ResetFight();
        }

        // ------------------------------------------------------------------ Simulacrum Boss Rush

        /// <summary>Runs Boss Rush in the Simulacrum. Returns true while it owns the music.</summary>
        private bool UpdateBossRush(float now, float dt)
        {
            var run = Run.instance as InfiniteTowerRun;
            if (!run)
            {
                bossRushWanted = false;
                return false;
            }
            bossRushTimer -= dt;
            if (bossRushTimer <= 0f)
            {
                bossRushTimer = 0.5f;
                bossRushWanted = ModConfig.SimulacrumBossRush.Value && library.HasSlot(Slots.BossRush, Mode);
                if (!bossRushWanted)
                {
                    if (kind == Kind.BossRush) Stop(1.5f);
                    return false;
                }
                int tier = BossPhases.BossRushTier(run.waveIndex);
                // (A song that is still loading has no voice yet, so give it a few seconds before retrying.)
                bool silent = engine.ActiveMusicVoice == 0 && now - stateStart > 3f;
                if (kind != Kind.BossRush || tier != bossRushTier || silent) StartBossRush(tier, now);
            }
            return bossRushWanted;
        }

        private void StartBossRush(int tier, float now)
        {
            string context = "bossrush:" + tier;
            var track = HostPick(context) ?? library.PickTier(Slots.BossRush, tier, Mode);
            if (track == null) return;
            bool first = kind != Kind.BossRush;
            bool newTier = first || tier != bossRushTier;
            if (currentTrack == null || track.Path != currentTrack.Path || engine.ActiveMusicVoice == 0)
                PlayTrack(track, true, first ? Transition.Drop(ImpactStrength, 0.03f) : Transition.Crossfade(2.5f), false, context);
            VanillaMusic.FadeOut();
            ambientActive = false;
            ResetFight();
            state = State.Playing;
            kind = Kind.BossRush;
            theme = null;
            bossRushTier = tier;
            stateStart = now;
            if (newTier && ModConfig.BossTitleCards.Value)
                titleCard?.Show("Boss Rush", "Tier " + tier, BannerStyles.BossRush, 1.1f + 0.1f * tier);
            Log.Info($"Boss Rush tier {tier} -> {track}");
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
                currentContext = "stage:" + stage.Key;
                return;
            }
            if (now < ambientRetryAfter) return;
            StartAmbient(now, Transition.Crossfade(ambientActive && playing ? 2.5f : 2f));
        }

        private bool StartAmbient(float now, Transition transition)
        {
            if (!StageMusicWanted(out var stage)) return false;
            string context = "stage:" + stage.Key;
            var track = HostPick(context, ambientTrack) ?? library.PickStage(stage.Slots, Mode, ambientTrack);
            if (track == null)
            {
                ambientRetryAfter = now + 10f;
                return false;
            }
            PlayTrack(track, LoopSongs, transition, true, context);
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
            currentContext = null;
            engine.StopAll(fade);
            VanillaMusic.FadeIn();
        }

        // ------------------------------------------------------------------ multiplayer

        /// <summary>Client: the host picked a different song for what we're playing now; follow it if we have the file.</summary>
        private void OnHostChoice(string context, string rel)
        {
            if (context == null || context != currentContext || engine.ActiveMusicVoice == 0) return;
            if (state != State.Playing && !(state == State.Idle && ambientActive)) return;
            var t = library.FindByRelPath(rel);
            if (t == null || library.IsBanned(t) || currentTrack == null) return;
            if (t.Path == currentTrack.Path || SameSong(t, currentTrack)) return;

            if (state == State.Idle)
            {
                PlayTrack(t, LoopSongs, Transition.Crossfade(1.5f), true, context);
                ambientTrack = t;
            }
            else if (kind == Kind.BossRush)
            {
                PlayTrack(t, true, Transition.Crossfade(1.5f), false, context);
            }
            else if (kind == Kind.Escape)
            {
                PlayTrack(t, true, Transition.Crossfade(1.5f), true, context);
            }
            else
            {
                PlayBossTrack(t, Transition.Crossfade(1.5f), context);
            }
            Log.Info("Following the host's song: " + t);
        }

        private static bool SameSong(TrackInfo a, TrackInfo b) =>
            a.Sequence != null && a.Sequence == b.Sequence
            && string.Equals(a.Slot, b.Slot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Pack, b.Pack, StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ the local player

        private void UpdateLocalPlayer(float now)
        {
            CharacterBody body = null;
            try { body = LocalUserManager.GetFirstLocalUser()?.cachedBody; } catch { /* no local user yet */ }
            var hc = body ? body.healthComponent : null;
            bool aliveNow = hc && hc.alive;

            // Low health: the music muffles and a heartbeat fades in below 25%.
            float low = 0f;
            if (aliveNow && ModConfig.LowHealthEffect.Value && Run.instance && !runOver && kind != Kind.Preview)
                low = Mathf.Clamp01((0.25f - hc.combinedHealthFraction) / 0.25f);
            engine.LowHealth = low;

            if (Run.instance && !runOver && body && body == lastBody && lastAlive && !aliveNow)
            {
                // Died: wind the music down like a stopped tape.
                deadSince = now;
                if (ModConfig.TapeStopOnDeath.Value && engine.AnythingPlaying && !tapeStopped)
                {
                    tapeStopped = true;
                    engine.TapeStop(1.1f);
                }
            }
            if (tapeStopped)
            {
                // Revived (Dio's, a new body), or still spectating teammates: spin back up.
                if (aliveNow) Spin(0.7f);
                else if (now - deadSince > 3f && TeammateAlive(body)) Spin(0.9f);
            }
            if (body)
            {
                lastBody = body;
                lastAlive = aliveNow;
            }
        }

        private void Spin(float seconds)
        {
            tapeStopped = false;
            engine.TapeStart(seconds);
        }

        private static bool TeammateAlive(CharacterBody self)
        {
            foreach (var pc in PlayerCharacterMasterController.instances)
            {
                if (!pc || !pc.master) continue;
                var b = pc.master.GetBody();
                if (b && b != self && b.healthComponent && b.healthComponent.alive) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ hotkeys

        private void HandleHotkeys(float now)
        {
            bool skip = Pressed(ModConfig.SkipKey), fav = Pressed(ModConfig.FavoriteKey), ban = Pressed(ModConfig.BanKey);
            if (!skip && !fav && !ban) return;
            if (Typing()) return;

            var track = engine.ActiveMusicVoice != 0 && (state == State.Playing || ambientActive) ? currentTrack : null;
            if (track == null)
            {
                banner.ShowHint("No Calamity song is playing right now.");
                return;
            }
            if (fav)
            {
                bool on = library.ToggleFavorite(track);
                banner.ShowHint((on ? "Favourited: " : "Removed from favourites: ") + track.Display);
            }
            if (ban)
            {
                library.BanSong(track);
                banner.ShowHint("Banned: " + track.Display + ". It won't play again.");
                Skip(track, true, now);
            }
            else if (skip)
            {
                Skip(track, false, now);
            }
        }

        private void Skip(TrackInfo current, bool banned, float now)
        {
            TrackInfo next = null;
            if (state == State.Idle && ambientActive && ambientTheme != null)
            {
                next = library.PickStage(ambientTheme.Slots, Mode, current);
                if (next != null && next.Path != current.Path)
                {
                    PlayTrack(next, LoopSongs, Transition.Crossfade(1.5f), true, currentContext);
                    ambientTrack = next;
                    return;
                }
            }
            else if (state == State.Playing && kind == Kind.BossRush)
            {
                next = library.PickTier(Slots.BossRush, bossRushTier, Mode);
                if (next != null && next.Path != current.Path)
                {
                    PlayTrack(next, true, Transition.Crossfade(1.5f), false, currentContext);
                    return;
                }
                if (!banned) banner.ShowHint("Boss Rush follows the waves: it moves on by itself.");
                return;
            }
            else if (state == State.Playing && kind == Kind.Escape)
            {
                next = library.PickEscape(Mode);
                if (next != null && next.Path != current.Path)
                {
                    PlayTrack(next, true, Transition.Crossfade(1.5f), true, currentContext);
                    return;
                }
            }
            else if (state == State.Playing && theme != null)
            {
                next = PickFor(theme, current, false);
                if (next != null && next.Path != current.Path && !SameSong(next, current))
                {
                    PlayBossTrack(next, Transition.Crossfade(1.5f), BossContext(theme));
                    return;
                }
            }

            if (!banned)
            {
                banner.ShowHint("No other songs for this one yet.");
                return;
            }
            // Banned the only song there was: stop it.
            if (state == State.Playing) End(now);
            else if (ambientActive)
            {
                StopAmbient(1.5f);
                ambientRetryAfter = now + 2f;
            }
        }

        private static bool Pressed(ConfigEntry<KeyboardShortcut> entry)
        {
            try
            {
                var k = entry.Value;
                return k.MainKey != KeyCode.None && k.IsDown();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True while a text box (chat, a settings field) has focus, so typing never triggers hotkeys.</summary>
        private static bool Typing()
        {
            try
            {
                foreach (var es in MPEventSystem.readOnlyInstancesList)
                {
                    var go = es ? es.currentSelectedGameObject : null;
                    if (go && (go.GetComponent<TMPro.TMP_InputField>() || go.GetComponent<UnityEngine.UI.InputField>())) return true;
                }
            }
            catch
            {
                // no event systems yet
            }
            return false;
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
                                var other = library.PickStage(ambientTheme.Slots, Mode, e.Track);
                                if (other != null)
                                {
                                    PlayTrack(other, false, Transition.Crossfade(3f), true, currentContext);
                                    ambientTrack = other;
                                }
                            }
                            break;
                        }
                        if (state != State.Playing || e.VoiceId != engine.ActiveMusicVoice) break;
                        if (phaseLocked || kind == Kind.BossRush) break; // the part loops until the fight moves on
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
            if (kind == Kind.BossRush)
            {
                // Boss Rush restarts its tier on the next update.
                bossRushTimer = 0f;
                return;
            }
            if (kind == Kind.Escape)
            {
                var esc = library.PickEscape(Mode);
                if (esc != null) PlayTrack(esc, true, transition, true, "escape");
                return;
            }
            var next = theme != null ? PickFor(theme, currentTrack, false) : null;
            if (next == null) return;
            if (kind == Kind.Preview) PlayTrack(next, LoopSongs, transition);
            else PlayBossTrack(next, transition, BossContext(theme));
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
                banner.ShowHint("Music is set to Vanilla. Pick Auto, Calamity, Infernum or Mixed first.");
                return;
            }
            warnedThisStage = false;
            if (LibraryEmpty()) return;

            string slot = library.RandomFilledSlot(Mode);
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

        public void ShowTestTitleCard()
        {
            titleCard?.ShowSample();
        }

        public void ClearBans()
        {
            int n = library.BannedCount;
            library.ClearBans();
            library.RescanAsync();
            banner.ShowHint(n == 0 ? "No banned songs." : $"Unbanned {n} song{(n == 1 ? "" : "s")}.");
        }
    }
}
