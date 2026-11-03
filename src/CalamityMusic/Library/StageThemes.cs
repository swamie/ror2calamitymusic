using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;

namespace CalamityMusic.Library
{
    /// <summary>
    /// Maps Risk of Rain 2 scenes (stages, menus, Bazaar, Void, DLC and modded stages) to Calamity
    /// area ("biome") themes. Lives in BepInEx/config/CalamityMusic.StageThemes.cfg; stages the mod
    /// hasn't seen before are added the first time you visit them, with a guess from their name.
    /// </summary>
    internal sealed class StageThemes
    {
        private const string Section = "Stage Themes";

        private static readonly (string scene, string slots, string note)[] Defaults =
        {
            // Menus
            ("title", "Biome_Title", "Main menu"),
            ("lobby", "Biome_Title", "Character select"),
            ("logbook", "Biome_Title", "Logbook"),
            ("eclipseworld", "Biome_Title", "Eclipse menu"),
            ("infinitetowerworld", "Biome_Title", "Simulacrum menu"),
            ("outro", "Biome_Credits", "Ending and credits"),
            ("intro", "Vanilla", "Opening cutscene"),
            ("splash", "Vanilla", "Splash screen"),

            // Stage 1
            ("golemplains", "Biome_SunkenSea", "Titanic Plains: calm open grassland"),
            ("golemplains2", "Biome_SunkenSea", "Titanic Plains"),
            ("blackbeach", "Biome_SulphurousNight, Biome_SulphurousRain", "Distant Roost: dark, rainy shore"),
            ("blackbeach2", "Biome_SulphurousNight, Biome_SulphurousRain", "Distant Roost"),
            ("snowyforest", "Biome_Blizzard", "Siphoned Forest: snow"),
            ("lakes", "Biome_SunkenSea", "Verdant Falls: water and greenery"),
            ("lakesnight", "Biome_SulphurousNight", "Viscous Falls: the night version"),
            ("village", "Biome_Rift", "Shattered Abodes"),
            ("villagenight", "Biome_Rift", "Disturbed Impact"),

            // Stage 2
            ("goolake", "Biome_SulphurousSea", "Abandoned Aqueduct: tar and desert"),
            ("foggyswamp", "Biome_SulphurousRain, Biome_Rift", "Wetland Aspect: fog and swamp"),
            ("ancientloft", "Biome_Stars, Biome_Astral", "Aphelian Sanctuary: temple above the clouds"),
            ("lemuriantemple", "Biome_BrimstoneCrags", "Reformed Altar: lemurian fire temple"),

            // Stage 3
            ("frozenwall", "Biome_Lab, Biome_Blizzard", "Rallypoint Delta: frozen military base"),
            ("wispgraveyard", "Biome_BrimstoneCrags", "Scorched Acres: burnt ground"),
            ("sulfurpools", "Biome_SulphurousSea, Biome_AcidRain", "Sulfur Pools: literally the Sulphurous Sea"),
            ("habitat", "Biome_AstralUnderground", "Treeborn Colony"),
            ("habitatfall", "Biome_EternalGarden", "Golden Dieback"),

            // Stage 4
            ("dampcavesimple", "Biome_Abyss2, Biome_Abyss3", "Abyssal Depths: deep caves"),
            ("shipgraveyard", "Biome_Abyss1, Biome_SulphurousNight", "Siren's Call: shipwrecks by the sea"),
            ("rootjungle", "Biome_AstralUnderground, Biome_SunkenSea", "Sundered Grove: glowing alien jungle"),
            ("helminthroost", "Biome_BrimstoneCrags", "Helminth Hatchery: lava"),

            // Stage 5 and final stages
            ("skymeadow", "Biome_EternalGarden, Biome_Stars", "Sky Meadow: islands in the sky"),
            ("moon", "Biome_Astral", "Commencement"),
            ("moon2", "Biome_Astral", "Commencement"),
            ("meridian", "Biome_EternalGarden", "Prime Meridian"),

            // Hidden realms and in-between places
            ("bazaar", "Biome_SunkenSea", "Bazaar Between Time: a calm shop"),
            ("goldshores", "Biome_EternalGarden", "Gilded Coast"),
            ("mysteryspace", "Biome_Rift", "A Moment, Fractured"),
            ("limbo", "Biome_Rift, Biome_Stars", "A Moment, Whole"),
            ("artifactworld", "Biome_Lab", "Bulwark's Ambry"),
            ("artifactworld01", "Biome_Lab", "Bulwark's Ambry"),
            ("artifactworld02", "Biome_Lab", "Bulwark's Ambry"),
            ("artifactworld03", "Biome_Lab", "Bulwark's Ambry"),

            // Void
            ("arena", "Biome_Abyss3", "Void Fields"),
            ("voidstage", "Biome_Abyss4", "Void Locus"),
            ("voidraid", "Biome_Abyss4", "The Planetarium"),

            // Simulacrum (void-infested copies of the stages)
            ("itgolemplains", "Biome_Abyss3, Biome_Abyss4", "Simulacrum"),
            ("itgoolake", "Biome_Abyss3, Biome_Abyss4", "Simulacrum"),
            ("itancientloft", "Biome_Abyss3, Biome_Abyss4", "Simulacrum"),
            ("itfrozenwall", "Biome_Abyss3, Biome_Abyss4", "Simulacrum"),
            ("itdampcave", "Biome_Abyss3, Biome_Abyss4", "Simulacrum"),
            ("itskymeadow", "Biome_Abyss3, Biome_Abyss4", "Simulacrum"),
            ("itmoon", "Biome_Abyss3, Biome_Abyss4", "Simulacrum"),
        };

        /// <summary>Name-based guesses for stages that aren't in the table (new DLC stages, modded stages).</summary>
        private static readonly (string[] words, string slots)[] Keywords =
        {
            (new[] { "void", "abyss" }, "Biome_Abyss4"),
            (new[] { "snow", "frozen", "frost", "ice", "cold", "winter", "tundra" }, "Biome_Blizzard"),
            (new[] { "lava", "magma", "fire", "hell", "scorch", "burn", "molten", "volcan", "ash", "inferno" }, "Biome_BrimstoneCrags"),
            (new[] { "lab", "factory", "conduit", "computational", "crater", "station", "base", "solus", "forge", "machine", "foundry", "facility" }, "Biome_Lab"),
            (new[] { "night", "dusk" }, "Biome_SulphurousNight"),
            (new[] { "swamp", "rain", "bog", "marsh", "storm" }, "Biome_SulphurousRain"),
            (new[] { "lunar", "moon", "sky", "star", "space", "astral", "cosmic" }, "Biome_Astral"),
            (new[] { "sulfur", "sulphur", "tar", "desert", "sand", "dune", "waste" }, "Biome_SulphurousSea"),
            (new[] { "cave", "depth", "deep", "underground", "mine" }, "Biome_Abyss2"),
            (new[] { "lake", "sea", "water", "beach", "shore", "coast", "falls", "river", "ocean", "reef" }, "Biome_SunkenSea"),
            (new[] { "garden", "meadow", "grove", "forest", "jungle", "gold", "temple" }, "Biome_EternalGarden"),
        };

        private readonly ConfigFile file;
        private readonly Dictionary<string, ConfigEntry<string>> entries = new Dictionary<string, ConfigEntry<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BossTheme> cache = new Dictionary<string, BossTheme>(StringComparer.OrdinalIgnoreCase);

        public StageThemes(string configDir)
        {
            file = new ConfigFile(Path.Combine(configDir, "CalamityMusic.StageThemes.cfg"), true);
            foreach (var d in Defaults)
            {
                entries[d.scene] = file.Bind(Section, d.scene, d.slots,
                    d.note + ". Comma-separated area theme folders (Biome_...), one picked at random. Use Vanilla to keep the game's music here. Biome_Any picks from every area theme.");
            }
            file.SettingChanged += (_, __) => cache.Clear();
        }

        public void Reload()
        {
            file.Reload();
            cache.Clear();
        }

        public BossTheme Resolve(string sceneName, string displayName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            if (cache.TryGetValue(sceneName, out var cached)) return cached;

            if (!entries.TryGetValue(sceneName, out var entry))
            {
                string guess = Guess(sceneName + " " + displayName);
                string note = string.IsNullOrEmpty(displayName) ? "Auto-added" : "Auto-added: " + displayName;
                entry = file.Bind(Section, Sanitize(sceneName), guess,
                    note + " (first visited in game, theme guessed from its name). Comma-separated area theme folders. Use Vanilla to keep the game's music.");
                entries[sceneName] = entry;
                Log.Info($"New stage '{sceneName}' ({displayName}) mapped to [{guess}]. Edit CalamityMusic.StageThemes.cfg to change it.");
            }

            var slots = (entry.Value ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            bool vanilla = slots.Length == 0 || slots.Any(s => s.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) || s.Equals("None", StringComparison.OrdinalIgnoreCase));
            var theme = new BossTheme { Key = sceneName, Slots = slots, KeepVanilla = vanilla, Priority = 0 };
            cache[sceneName] = theme;
            return theme;
        }

        private static string Guess(string name)
        {
            string hay = (name ?? "").ToLowerInvariant();
            foreach (var k in Keywords)
                if (k.words.Any(w => hay.Contains(w))) return k.slots;
            return Slots.AnyBiome;
        }

        private static string Sanitize(string key)
        {
            foreach (char c in new[] { '=', '\n', '\t', '\\', '"', '\'', '[', ']' })
                key = key.Replace(c, '_');
            return key;
        }
    }
}
