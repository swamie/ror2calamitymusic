using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using CalamityMusic.Audio;
using CalamityMusic.Game;
using CalamityMusic.Library;
using CalamityMusic.UI;
using HarmonyLib;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;

namespace CalamityMusic
{
    [BepInPlugin(Guid, ModName, Version)]
    [BepInDependency("com.rune580.riskofoptions")]
    [BepInDependency(Starstorm2Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class CalamityMusicPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.swamie.calamitymusic";
        public const string ModName = "CalamityMusic";
        public const string Version = "1.0.0";
        private const string Starstorm2Guid = "com.TeamMoonstorm.Starstorm2";

        private MusicEngine engine;
        private MusicLibrary library;
        private BossThemes themes;
        private StageThemes stageThemes;
        private MusicDirector director;
        private NowPlayingBanner banner;
        private Harmony harmony;

        private void Awake()
        {
            Log.Init(Logger);
            ModConfig.Bind(Config);

            string folder = ModConfig.MusicFolderOverride.Value;
            if (string.IsNullOrWhiteSpace(folder)) folder = Path.Combine(Paths.ConfigPath, "CalamityMusic", "Music");
            library = new MusicLibrary(folder.Trim());
            try
            {
                library.EnsureFolders();
            }
            catch (Exception e)
            {
                Log.Error("Could not create the music folder at " + library.Root + ": " + e.Message);
            }
            library.RescanAsync();

            themes = new BossThemes(Paths.ConfigPath);
            stageThemes = new StageThemes(Paths.ConfigPath);

            ApplyPatches();

            engine = new MusicEngine();
            engine.Start();

            banner = gameObject.AddComponent<NowPlayingBanner>();
            banner.Init(() => engine.BassLevel);
            director = gameObject.AddComponent<MusicDirector>();
            director.Init(engine, library, themes, stageThemes, banner);

            SetupSettingsMenu();

            if (Chainloader.PluginInfos.ContainsKey(Starstorm2Guid))
                Log.Info("Starstorm 2 detected: Wayfarer, Nemesis and Empyrean fights have Calamity themes.");
            Log.Info("Music folder: " + library.Root);
        }

        /// <summary>
        /// Patches class by class so one failure (after a game update, say) doesn't take the rest down,
        /// then switches off whatever depends on a patch that didn't apply.
        /// </summary>
        private void ApplyPatches()
        {
            harmony = new Harmony(Guid);
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(CalamityMusicPlugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0 &&
                    !type.GetMethods(AccessTools.all).Any(m => m.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
                    continue;
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.Error("Patch " + type.Name + " failed: " + e.Message);
                }
            }

            bool volumePatched = IsPatched(AccessTools.Method(typeof(RoR2.AudioManager.VolumeConVar), nameof(RoR2.AudioManager.VolumeConVar.GetString)))
                                 && IsPatched(AccessTools.Method(typeof(RoR2.AudioManager.VolumeConVar), nameof(RoR2.AudioManager.VolumeConVar.SetString)));
            VanillaMusic.Enabled = volumePatched;
            if (!volumePatched)
                Log.Warning("Music volume patches did not apply, so the game's own music will keep playing underneath.");

            BossGroupTracker.Patched = IsPatched(AccessTools.Method(typeof(RoR2.BossGroup), nameof(RoR2.BossGroup.Awake)));
            if (!BossGroupTracker.Patched)
                Log.Warning("BossGroup patch did not apply; falling back to scanning for boss groups.");
        }

        private static bool IsPatched(System.Reflection.MethodBase method)
        {
            if (method == null) return false;
            var info = Harmony.GetPatchInfo(method);
            return info != null && (info.Prefixes.Any(p => p.owner == Guid) || info.Postfixes.Any(p => p.owner == Guid));
        }

        private void SetupSettingsMenu()
        {
            try
            {
                var icon = LoadIcon();
                if (icon) ModSettingsManager.SetModIcon(icon);
                ModSettingsManager.SetModDescription(
                    "Calamity and Infernum boss music for teleporter events and boss fights, with a bass-boosted buildup and drop.\n\n" +
                    "Put songs in BepInEx/config/CalamityMusic/Music (use Open Music Folder below).");

                ModSettingsManager.AddOption(new ChoiceOption(ModConfig.Mode));
                ModSettingsManager.AddOption(new SliderOption(ModConfig.Volume, new SliderConfig { min = 0f, max = 100f, FormatString = "{0:0}%" }));
                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.FollowGameVolume));
                ModSettingsManager.AddOption(new StepSliderOption(ModConfig.BassBoost, new StepSliderConfig { min = 0f, max = 15f, increment = 0.5f, FormatString = "{0:0.0} dB" }));
                ModSettingsManager.AddOption(new StepSliderOption(ModConfig.BassFrequency, new StepSliderConfig { min = 40f, max = 200f, increment = 5f, FormatString = "{0:0} Hz" }));
                ModSettingsManager.AddOption(new GenericButtonOption("Preview", "General",
                    "Play a buildup and drop into a random song from your library, to tune volume and bass from the menu. Press again to stop.",
                    "Play / Stop", director.TogglePreview));
                ModSettingsManager.AddOption(new GenericButtonOption("Get Songs", "General",
                    "Downloads the free Calamity, Infernum and Wrath of the Gods music mods from Steam Workshop and files every boss theme into the right folder. Opens a window; needs Python 3 (it opens the download page if you don't have it). Songs load by themselves when it finishes.",
                    "Get Songs", RunSongInstaller));
                ModSettingsManager.AddOption(new GenericButtonOption("Open Music Folder", "General",
                    "Opens the folder where your Calamity songs go. HOW_TO_ADD_SONGS.txt inside lists every boss folder and which song fits.",
                    "Open", OpenMusicFolder));
                ModSettingsManager.AddOption(new GenericButtonOption("Rescan Music Folder", "General",
                    "Reload songs and boss theme mappings after you change files.",
                    "Rescan", () =>
                    {
                        library.RescanAsync();
                        themes.Reload();
                        stageThemes.Reload();
                    }));

                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.BuildupEnabled));
                ModSettingsManager.AddOption(new ChoiceOption(ModConfig.Buildup));
                ModSettingsManager.AddOption(new StepSliderOption(ModConfig.BuildupLength, new StepSliderConfig { min = 2f, max = 12f, increment = 0.5f, FormatString = "{0:0.0} s" }));
                ModSettingsManager.AddOption(new SliderOption(ModConfig.ImpactStrength, new SliderConfig { min = 0f, max = 100f, FormatString = "{0:0}%" }));

                ModSettingsManager.AddOption(new ChoiceOption(ModConfig.EndsWhen));
                ModSettingsManager.AddOption(new ChoiceOption(ModConfig.OnSongEnd));
                ModSettingsManager.AddOption(new StepSliderOption(ModConfig.FadeOutSeconds, new StepSliderConfig { min = 0.5f, max = 10f, increment = 0.5f, FormatString = "{0:0.0} s" }));
                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.MuffleWhenPaused));
                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.AnySongFallback));
                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.MoonEscapeMusic));

                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.StageMusic));
                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.MenuMusic));
                ModSettingsManager.AddOption(new SliderOption(ModConfig.StageVolume, new SliderConfig { min = 0f, max = 100f, FormatString = "{0:0}%" }));
                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.ShowStageNowPlaying));

                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.ShowNowPlaying));
                ModSettingsManager.AddOption(new StepSliderOption(ModConfig.NowPlayingSeconds, new StepSliderConfig { min = 2f, max = 15f, increment = 0.5f, FormatString = "{0:0.0} s" }));
                ModSettingsManager.AddOption(new ChoiceOption(ModConfig.NowPlayingPosition));
                ModSettingsManager.AddOption(new SliderOption(ModConfig.NowPlayingScale, new SliderConfig { min = 50f, max = 200f, FormatString = "{0:0}%" }));
                ModSettingsManager.AddOption(new CheckBoxOption(ModConfig.NowPlayingPulse));
                ModSettingsManager.AddOption(new GenericButtonOption("Test Banner", "Now Playing",
                    "Shows the Now Playing banner so you can check its position and size.", "Show", director.ShowTestBanner));
            }
            catch (Exception e)
            {
                Log.Error("Could not build the settings menu: " + e);
            }
        }

        private Sprite LoadIcon()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Info.Location) ?? "", "icon.png");
                if (!File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Log.Warning("Could not load icon: " + e.Message);
                return null;
            }
        }

        private void RunSongInstaller()
        {
            try
            {
                string bat = Path.Combine(Path.GetDirectoryName(Info.Location) ?? "", "tools", "Get Calamity Songs.bat");
                if (Application.platform != RuntimePlatform.WindowsPlayer || !File.Exists(bat))
                {
                    Log.Warning("Song installer not found at " + bat + "; opening the instructions instead.");
                    Application.OpenURL("https://github.com/swamie/ror2calamitymusic/blob/main/SONGS.md");
                    return;
                }
                Directory.CreateDirectory(library.Root);
                // cmd /c ""script.bat" --music "path"" : cmd strips the outer quotes.
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\" --music \"" + library.Root + "\"\"")
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(bat),
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception e)
            {
                Log.Error("Could not start the song installer: " + e.Message);
            }
        }

        private void OpenMusicFolder()
        {
            try
            {
                Directory.CreateDirectory(library.Root);
                Application.OpenURL("file:///" + library.Root.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Log.Error("Could not open " + library.Root + ": " + e.Message);
            }
        }

        private void OnApplicationQuit() => Shutdown();

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            VanillaMusic.Restore();
            engine?.Dispose();
            engine = null;
        }
    }

    internal static class Log
    {
        private static ManualLogSource source;

        public static void Init(ManualLogSource s) => source = s;
        public static void Info(string msg) => source?.LogInfo(msg);
        public static void Warning(string msg) => source?.LogWarning(msg);
        public static void Error(string msg) => source?.LogError(msg);
    }
}
