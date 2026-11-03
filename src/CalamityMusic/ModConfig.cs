using BepInEx.Configuration;
using CalamityMusic.Library;
using UnityEngine;

namespace CalamityMusic
{
    internal enum BuildupStyle
    {
        Synth,
        CustomFiles,
        Random,
    }

    internal enum MusicEndsWhen
    {
        TeleporterCharged,
        BossDefeated,
    }

    internal enum SongEndBehaviour
    {
        LoopSong,
        PlayAnotherFromTheme,
    }

    internal enum BannerPosition
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
    }

    internal static class ModConfig
    {
        // General
        public static ConfigEntry<MusicMode> Mode;
        public static ConfigEntry<float> Volume;
        public static ConfigEntry<bool> FollowGameVolume;
        public static ConfigEntry<float> BassBoost;
        public static ConfigEntry<float> BassFrequency;

        // Teleporter buildup
        public static ConfigEntry<bool> BuildupEnabled;
        public static ConfigEntry<BuildupStyle> Buildup;
        public static ConfigEntry<float> BuildupLength;
        public static ConfigEntry<float> ImpactStrength;

        // Playback
        public static ConfigEntry<MusicEndsWhen> EndsWhen;
        public static ConfigEntry<SongEndBehaviour> OnSongEnd;
        public static ConfigEntry<float> FadeOutSeconds;
        public static ConfigEntry<bool> MuffleWhenPaused;
        public static ConfigEntry<bool> AnySongFallback;
        public static ConfigEntry<bool> MoonEscapeMusic;

        // Boss fights
        public static ConfigEntry<bool> FollowBossPhases;
        public static ConfigEntry<bool> LastStand;
        public static ConfigEntry<bool> SimulacrumBossRush;
        public static ConfigEntry<bool> BossTitleCards;

        // Effects
        public static ConfigEntry<bool> TapeStopOnDeath;
        public static ConfigEntry<bool> LowHealthEffect;

        // Hotkeys
        public static ConfigEntry<KeyboardShortcut> SkipKey;
        public static ConfigEntry<KeyboardShortcut> FavoriteKey;
        public static ConfigEntry<KeyboardShortcut> BanKey;

        // Multiplayer
        public static ConfigEntry<bool> MultiplayerSync;

        // Stage music
        public static ConfigEntry<bool> StageMusic;
        public static ConfigEntry<bool> MenuMusic;
        public static ConfigEntry<float> StageVolume;
        public static ConfigEntry<bool> ShowStageNowPlaying;

        // Now playing
        public static ConfigEntry<bool> ShowNowPlaying;
        public static ConfigEntry<float> NowPlayingSeconds;
        public static ConfigEntry<BannerPosition> NowPlayingPosition;
        public static ConfigEntry<float> NowPlayingScale;
        public static ConfigEntry<bool> NowPlayingPulse;
        public static ConfigEntry<float> NowPlayingOffsetX;
        public static ConfigEntry<float> NowPlayingOffsetY;
        public static ConfigEntry<bool> NowPlayingEffects;
        public static ConfigEntry<bool> NowPlayingThemeName;

        // Advanced
        public static ConfigEntry<string> MusicFolderOverride;

        public static void Bind(ConfigFile cfg)
        {
            const string G = "General";
            Mode = cfg.Bind(G, "Music", MusicMode.Auto,
                "Which music plays during teleporter events and boss fights.\n" +
                "Vanilla = normal Risk of Rain 2 music. Auto = Calamity songs, switching to Infernum on Monsoon and Eclipse. Calamity = Calamity songs. " +
                "Infernum = Infernum versions where you have them, Calamity songs otherwise. Mixed = everything shuffled together.");
            Volume = cfg.Bind(G, "Music Volume", 70f, "Volume of the Calamity music, 0-100%.");
            FollowGameVolume = cfg.Bind(G, "Follow In-Game Volume", true,
                "Also scale by the game's own Master and Music volume settings.");
            BassBoost = cfg.Bind(G, "Bass Boost", 7f, "Live bass boost in dB (0 = off). A limiter keeps it from clipping.");
            BassFrequency = cfg.Bind(G, "Bass Boost Frequency", 90f, "Center of the bass boost in Hz. Lower = deeper sub, higher = punchier.");

            const string B = "Teleporter Buildup";
            BuildupEnabled = cfg.Bind(B, "Enable Buildup", true,
                "Play a buildup while the teleporter turns red, then drop into the boss theme.");
            Buildup = cfg.Bind(B, "Buildup Style", BuildupStyle.Random,
                "Synth = generated riser. CustomFiles = random file from Music/Buildups (falls back to Synth if empty). Random = either.");
            BuildupLength = cfg.Bind(B, "Buildup Length", 5f,
                "Seconds from activating the teleporter to the drop. The drop waits for the boss to actually spawn.");
            ImpactStrength = cfg.Bind(B, "Drop Impact", 80f, "Loudness of the bass hit layered on the drop, 0-100% (0 = off).");

            const string P = "Playback";
            EndsWhen = cfg.Bind(P, "Teleporter Music Ends When", MusicEndsWhen.TeleporterCharged,
                "TeleporterCharged = keep the boss theme going until the teleporter hits 100% (and every boss is dead). BossDefeated = stop as soon as the bosses die.");
            OnSongEnd = cfg.Bind(P, "When A Song Ends", SongEndBehaviour.LoopSong,
                "LoopSong = loop it seamlessly like in Terraria. PlayAnotherFromTheme = crossfade into another song for the same boss.");
            FadeOutSeconds = cfg.Bind(P, "Fade Out Time", 3f, "Seconds to fade the Calamity music out after a fight.");
            MuffleWhenPaused = cfg.Bind(P, "Muffle When Paused", true, "Muffle and lower the music while the pause menu is open.");
            AnySongFallback = cfg.Bind(P, "Use Any Song As Fallback", true,
                "If a boss has no songs in its theme folders (or in Generic), play any song from your library instead of the vanilla music.");
            MoonEscapeMusic = cfg.Bind(P, "Moon Escape Music", true,
                "Play a song from the MoonEscape folder during the escape after Mithrix. If the folder is empty the vanilla escape music plays.");

            const string F = "Boss Fights";
            FollowBossPhases = cfg.Bind(F, "Follow Boss Phases", true,
                "Multi-part songs follow the fight: Mithrix's phases move through Supreme Calamitas' theme, Voidling's through the Avatar's, False Son's too.");
            LastStand = cfg.Bind(F, "Last Stand Kick", true,
                "When the last boss drops below 25% health the music jumps to its final part, or hits harder if the song has no parts.");
            SimulacrumBossRush = cfg.Bind(F, "Simulacrum Boss Rush", true,
                "Play the Boss Rush tiers in the Simulacrum, climbing a tier every 10 waves. Needs songs in the BossRush folder.");
            BossTitleCards = cfg.Bind(F, "Boss Title Cards", true,
                "Show an Infernum-style title card with the boss's name when its theme drops.");

            const string E = "Effects";
            TapeStopOnDeath = cfg.Bind(E, "Tape Stop On Death", true,
                "The music winds down like a stopped tape when you die, and spins back up if you're revived.");
            LowHealthEffect = cfg.Bind(E, "Low Health Muffle", true,
                "Below 25% health the music gets muffled and a heartbeat fades in.");

            const string H = "Hotkeys";
            SkipKey = cfg.Bind(H, "Skip Song", new KeyboardShortcut(KeyCode.F8), "Crossfade to another song for the same boss or stage.");
            FavoriteKey = cfg.Bind(H, "Favourite Song", new KeyboardShortcut(KeyCode.F9),
                "Favourite (or unfavourite) the song that's playing. Favourites come up three times as often. Saved in favorites.txt next to the Music folder.");
            BanKey = cfg.Bind(H, "Ban Song", new KeyboardShortcut(KeyCode.F10),
                "Never play this song again and skip it. Saved in banned.txt next to the Music folder; undo with Clear Banned Songs.");

            const string M = "Multiplayer";
            MultiplayerSync = cfg.Bind(M, "Sync With Host", true,
                "When the host also has this mod, play the same song the host's game picked (if you have that file).");

            const string St = "Stage Music";
            StageMusic = cfg.Bind(St, "Stage Music", true,
                "Replace the game's stage music too (every stage, Bazaar, Void, hidden realms, DLC and modded stages) with Calamity area themes. Which stage gets which theme is in CalamityMusic.StageThemes.cfg.");
            MenuMusic = cfg.Bind(St, "Menu Music", true, "Play the Calamity title theme on the main menu and character select.");
            StageVolume = cfg.Bind(St, "Stage Music Volume", 85f, "Stage music volume relative to boss music, 0-100%.");
            ShowStageNowPlaying = cfg.Bind(St, "Show Now Playing For Stage Music", true, "Show the song banner when a stage theme starts too.");

            const string N = "Now Playing";
            ShowNowPlaying = cfg.Bind(N, "Show Now Playing", true, "Show the song name banner when a boss theme starts.");
            NowPlayingSeconds = cfg.Bind(N, "Display Time", 6f, "Seconds the banner stays on screen.");
            NowPlayingPosition = cfg.Bind(N, "Screen Position", BannerPosition.BottomLeft,
                "Where the banner appears. BottomLeft sits just above the health bar, away from the abilities in the bottom right.");
            NowPlayingOffsetX = cfg.Bind(N, "Offset X", 0f, "Nudge the banner sideways, in pixels at 1080p.");
            NowPlayingOffsetY = cfg.Bind(N, "Offset Y", 0f, "Nudge the banner up (+) or down (-), in pixels at 1080p.");
            NowPlayingEffects = cfg.Bind(N, "Effects", true,
                "Per-song effects: particles themed to the boss or area (embers, bubbles, snow, stars, glitches, lightning...), letters popping in, light sweeps.");
            NowPlayingThemeName = cfg.Bind(N, "Show Boss Or Area Name", true, "Show which boss or area the song belongs to under the title.");
            NowPlayingScale = cfg.Bind(N, "Scale", 100f, "Banner size in %.");
            NowPlayingPulse = cfg.Bind(N, "Pulse With Bass", true, "Make the banner glow and bump to the bass.");

            const string A = "Advanced";
            MusicFolderOverride = cfg.Bind(A, "Music Folder", "",
                "Leave empty to use BepInEx/config/CalamityMusic/Music. Set a full path to keep your songs somewhere else.");
        }
    }
}
