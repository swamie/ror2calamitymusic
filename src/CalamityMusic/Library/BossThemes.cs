using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;

namespace CalamityMusic.Library
{
    internal sealed class BossTheme
    {
        public string Key;            // body name (or a special key like "Empyrean")
        public string[] Slots;
        public int Priority;          // higher wins when several bosses are alive; finales never get interrupted
        public float LingerSeconds;   // how long music keeps going after the last boss of a non-teleporter fight dies
        public bool KeepVanilla;      // user mapped this boss to "Vanilla"

        public bool IsFinale => Priority >= 100;

        public bool SharesSlotWith(BossTheme other)
        {
            if (other == null) return false;
            foreach (string s in Slots)
                if (other.Slots.Any(o => o.Equals(s, StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }
    }

    /// <summary>
    /// Maps Risk of Rain 2 (and Starstorm 2 / modded) bosses to Calamity theme folders.
    /// Everything lives in BepInEx/config/CalamityMusic.BossThemes.cfg so it can be edited without
    /// recompiling. Bosses the mod has never seen get an entry added the first time they show up,
    /// with a best guess based on their name.
    /// </summary>
    internal sealed class BossThemes
    {
        private const int Finale = 100, Special = 50, Normal = 10, Horde = 5;

        private struct Default
        {
            public string Body, Slots, Note;
            public int Priority;
        }

        private static readonly Default[] Defaults =
        {
            // ---- Final bosses
            D("BrotherBody", "SupremeCalamitas", Finale, "Mithrix (phases 1-3)"),
            D("BrotherHurtBody", "SupremeCalamitas", Finale, "Mithrix (phase 4, after stealing your items)"),
            D("VoidRaidCrabBody", "AvatarOfEmptiness, DevourerOfGods", Finale, "Voidling"),
            D("MiniVoidRaidCrabBodyPhase1", "AvatarOfEmptiness, DevourerOfGods", Finale, "Voidling phase 1"),
            D("MiniVoidRaidCrabBodyPhase2", "AvatarOfEmptiness, DevourerOfGods", Finale, "Voidling phase 2"),
            D("MiniVoidRaidCrabBodyPhase3", "AvatarOfEmptiness, DevourerOfGods", Finale, "Voidling phase 3"),
            D("FalseSonBossBody", "CalamitasClone, NamelessDeity", Finale, "False Son (a false copy, like the Calamitas Clone)"),
            D("FalseSonBossBodyLunarShard", "CalamitasClone, NamelessDeity", Finale, "False Son phase 2"),
            D("FalseSonBossBodyBrokenLunarShard", "CalamitasClone, NamelessDeity", Finale, "False Son phase 3"),

            // ---- Special encounters
            D("TitanGoldBody", "Providence, ProfanedGuardians", Special, "Aurelionite (golden, holy)"),
            D("ScavLunar1Body", "AstrumDeus, Astrageldon", Special, "Twisted Scavenger (lunar/astral)"),
            D("ScavLunar2Body", "AstrumDeus, Astrageldon", Special, "Twisted Scavenger (lunar/astral)"),
            D("ScavLunar3Body", "AstrumDeus, Astrageldon", Special, "Twisted Scavenger (lunar/astral)"),
            D("ScavLunar4Body", "AstrumDeus, Astrageldon", Special, "Twisted Scavenger (lunar/astral)"),
            D("ArtifactShellBody", "CeaselessVoid", Special, "Artifact Reliquary (stationary orb, like the Ceaseless Void)"),

            // ---- Teleporter bosses
            D("BeetleQueen2Body", "PlaguebringerGoliath, HiveMind", Normal, "Beetle Queen (insect queen)"),
            D("ClayBossBody", "DesertScourge, SlimeGod, Goozma", Normal, "Clay Dunestrider (dunes and tar)"),
            D("TitanBody", "Ravager", Normal, "Stone Titan (giant golem)"),
            D("VagrantBody", "LeviathanAnahita, AquaticScourge", Normal, "Wandering Vagrant (jellyfish -> aquatic)"),
            D("GravekeeperBody", "Polterghast, Crabulon", Normal, "Grovetender (wisps and spirits)"),
            D("ImpBossBody", "Signus", Normal, "Imp Overlord (teleporting void assassin)"),
            D("MagmaWormBody", "BrimstoneElemental", Normal, "Magma Worm (fire)"),
            D("ElectricWormBody", "StormWeaver", Normal, "Overloading Worm (lightning worm, like the Storm Weaver)"),
            D("RoboBallBossBody", "AstrumAureus", Normal, "Solus Control Unit (mech)"),
            D("SuperRoboBallBossBody", "ExoMechs", Normal, "Alloy Worship Unit (mech)"),
            D("GrandParentBody", "Yharon, ProfanedGuardians", Normal, "Grandparent (summons suns)"),
            D("ScavBody", "Ravager, Dragonfolly", Normal, "Scavenger"),
            D("MegaConstructBody", "ExoMechs, Mars", Normal, "Xi Construct (machine)"),
            D("VoidMegaCrabBody", "CeaselessVoid, Signus", Normal, "Void Devastator"),
            D("VoidJailerBody", "Signus", Normal, "Void Jailer"),

            // ---- Starstorm 2
            D("LampBossBody", "ProfanedGuardians, Polterghast", Normal, "Starstorm 2: Wayfarer, Usher of Light (ghostly lantern bearer)"),
            D("NemCommandoBody", "CalamitasClone", Special, "Starstorm 2: Nemesis Commando (dark doppelganger)"),
            D("NemMercBody", "Signus, CalamitasClone", Special, "Starstorm 2: Nemesis Mercenary (vengeful blade duelist)"),
        };

        /// <summary>Name-based guesses for bosses that aren't in the table (new DLC, modded, hordes).</summary>
        private static readonly (string[] words, string slots, int priority)[] Keywords =
        {
            (new[] { "brother", "mithrix" }, "SupremeCalamitas", Finale),
            (new[] { "voidraid" }, "AvatarOfEmptiness, DevourerOfGods", Finale),
            (new[] { "falseson" }, "CalamitasClone, NamelessDeity", Finale),
            (new[] { "solusheart" }, "ExoMechs, Mars", Finale),
            (new[] { "providence" }, "Providence", Finale),
            (new[] { "umbra", "shadowclone", "nem" }, "CalamitasClone", Special),
            (new[] { "solus", "construct", "robo", "drone", "mech", "hauler", "tanker", "probe", "unit" }, "ExoMechs", Normal),
            (new[] { "vulture", "bird", "harpy" }, "Dragonfolly", Normal),
            (new[] { "electric", "storm", "thunder", "lightning" }, "StormWeaver", Normal),
            (new[] { "halcyon", "gold", "holy", "sun", "lamp", "light" }, "ProfanedGuardians", Normal),
            (new[] { "jellyfish", "squid", "fish", "sea", "aqua", "water", "hermit", "crab" }, "AquaticScourge", Normal),
            (new[] { "beetle", "plague", "bee", "fly", "insect", "bug" }, "PlaguebringerGoliath", Normal),
            (new[] { "larva", "hive" }, "HiveMind", Normal),
            (new[] { "mush", "shroom", "fung" }, "Crabulon", Normal),
            (new[] { "gup", "geep", "gip", "slime", "goo", "tar", "ooze" }, "SlimeGod", Normal),
            (new[] { "clay", "sand", "desert", "dune" }, "DesertScourge", Normal),
            (new[] { "wisp", "ghost", "spirit", "soul", "haunt", "poltergeist", "hollow" }, "Polterghast", Normal),
            (new[] { "imp", "reaper", "jailer", "assassin" }, "Signus", Normal),
            (new[] { "void", "nullifier" }, "CeaselessVoid", Normal),
            (new[] { "lunar", "astral", "star", "cosmic" }, "AstrumDeus", Normal),
            (new[] { "magma", "fire", "flame", "lava", "scorch", "brimstone", "inferno" }, "BrimstoneElemental", Normal),
            (new[] { "ice", "frost", "snow", "cryo", "glacial" }, "Cryogen", Normal),
            (new[] { "dragon", "drake", "wyvern", "lemurian" }, "Yharon", Normal),
            (new[] { "blood", "flesh" }, "Perforators", Normal),
            (new[] { "worm", "wurm" }, "DesertScourge", Normal),
            (new[] { "titan", "golem", "stone", "rock", "geode", "parent", "bison", "scav" }, "Ravager", Normal),
        };

        private static Default D(string body, string slots, int priority, string note) =>
            new Default { Body = body, Slots = slots, Priority = priority, Note = note };

        private const string Section = "Boss Themes";
        private readonly ConfigFile file;
        private readonly Dictionary<string, ConfigEntry<string>> entries = new Dictionary<string, ConfigEntry<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BossTheme> cache = new Dictionary<string, BossTheme>(StringComparer.OrdinalIgnoreCase);

        public ConfigEntry<string> EmpyreanSlots { get; }

        public BossThemes(string configDir)
        {
            file = new ConfigFile(Path.Combine(configDir, "CalamityMusic.BossThemes.cfg"), true);
            foreach (var d in Defaults)
            {
                priorities[d.Body] = d.Priority;
                entries[d.Body] = file.Bind(Section, d.Body, d.Slots,
                    d.Note + ". Comma-separated theme folders; one is picked at random. Use Vanilla to keep the game's music for this boss.");
            }
            EmpyreanSlots = file.Bind("Special Encounters", "StarstormEmpyreanElite", "BossRush",
                "Theme for Starstorm 2 Empyrean elites (they get their own boss bar late in a run). Overrides the monster's normal theme.");
            file.SettingChanged += (_, __) => cache.Clear();
        }

        public void Reload()
        {
            file.Reload();
            cache.Clear();
        }

        public BossTheme Resolve(string bodyName, string displayName)
        {
            if (string.IsNullOrEmpty(bodyName)) bodyName = "UnknownBody";
            if (cache.TryGetValue(bodyName, out var cached)) return cached;

            if (!entries.TryGetValue(bodyName, out var entry))
            {
                // First time we see this boss: guess from its name and write it to the config for the player.
                Guess(bodyName, displayName, out string slots, out int prio);
                priorities[bodyName] = prio;
                string note = string.IsNullOrEmpty(displayName) ? "Auto-added" : "Auto-added: " + displayName;
                entry = file.Bind(Section, SanitizeKey(bodyName), slots,
                    note + " (first seen in game, theme guessed from its name). Comma-separated theme folders. Use Vanilla to keep the game's music.");
                entries[bodyName] = entry;
                Log.Info($"New boss '{bodyName}' ({displayName}) mapped to [{slots}]. Edit CalamityMusic.BossThemes.cfg to change it.");
            }

            var theme = Build(bodyName, entry.Value, priorities.TryGetValue(bodyName, out int p) ? p : Normal);
            cache[bodyName] = theme;
            return theme;
        }

        public BossTheme Custom(string key, string slots, int priority) => Build(key, slots, priority);

        public BossTheme Generic() => Build("Generic", Library.Slots.Generic, Horde);

        private static BossTheme Build(string key, string value, int priority)
        {
            var slots = (value ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            bool vanilla = slots.Any(s => s.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) || s.Equals("None", StringComparison.OrdinalIgnoreCase));
            if (slots.Length == 0) slots = new[] { Library.Slots.Generic };
            return new BossTheme
            {
                Key = key,
                Slots = slots,
                Priority = priority,
                KeepVanilla = vanilla,
                LingerSeconds = priority >= Finale ? 25f : priority >= Special ? 7f : 4f,
            };
        }

        private static void Guess(string bodyName, string displayName, out string slots, out int priority)
        {
            string hay = (bodyName + " " + displayName).ToLowerInvariant().Replace(" ", "");
            foreach (var k in Keywords)
            {
                if (k.words.Any(w => hay.Contains(w)))
                {
                    slots = k.slots;
                    priority = k.priority;
                    return;
                }
            }
            slots = Library.Slots.Generic;
            priority = Horde;
        }

        private static string SanitizeKey(string key)
        {
            foreach (char c in new[] { '=', '\n', '\t', '\\', '"', '\'', '[', ']' })
                key = key.Replace(c, '_');
            return key;
        }
    }
}
