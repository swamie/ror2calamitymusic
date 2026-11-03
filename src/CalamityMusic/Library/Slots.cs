using System.Collections.Generic;

namespace CalamityMusic.Library
{
    /// <summary>
    /// A "slot" is one Calamity boss theme and maps 1:1 to a folder name. Risk of Rain 2 bosses
    /// point at one or more slots; every song file inside a slot folder is a candidate.
    /// </summary>
    internal static class Slots
    {
        public const string Generic = "Generic";
        public const string Escape = "MoonEscape";
        public const string BossRush = "BossRush";

        /// <summary>Area themes live in folders starting with this; they're stage music, never boss music.</summary>
        public const string BiomePrefix = "Biome_";
        public const string AnyBiome = "Biome_Any";
        public const string Title = "Biome_Title";
        public const string Credits = "Biome_Credits";

        public static bool IsBiome(string slot) => slot != null && slot.StartsWith(BiomePrefix, System.StringComparison.OrdinalIgnoreCase);

        public sealed class SlotDef
        {
            public string Name;
            public string Boss;
            public string Suggested;          // base Calamity / music mod
            public string SuggestedInfernum;  // Infernum override, if one exists
            public bool ExtensionOnly;
            public bool IsBiome => Slots.IsBiome(Name);
        }

        public static readonly List<SlotDef> All = new List<SlotDef>
        {
            S("DesertScourge", "Desert Scourge", "DM DOKURO - Guardian of the Former Seas"),
            S("Crabulon", "Crabulon", "DM DOKURO - Infestation (in-game title: 1NF3$+@+!0N)"),
            S("HiveMind", "The Hive Mind", "DM DOKURO & SixteenInMono - The Filthy Mind"),
            S("Perforators", "The Perforators", "DM DOKURO & SixteenInMono - Blood Coagulant"),
            S("SlimeGod", "The Slime God", "DM DOKURO & SixteenInMono - Return to Slime"),
            S("Cryogen", "Cryogen", "DM DOKURO - Antarctic Reinforcement"),
            S("AquaticScourge", "Aquatic Scourge", "DM DOKURO - Outcast of the Sulphurous Seas"),
            S("BrimstoneElemental", "Brimstone Elemental", "DM DOKURO - Left Alone"),
            S("CalamitasClone", "Calamitas Clone", "DM DOKURO - Raw, Unfiltered Calamity"),
            S("LeviathanAnahita", "Leviathan and Anahita", "DM DOKURO - Forbidden Lullaby | DM DOKURO - Siren's War Cry"),
            S("AstrumAureus", "Astrum Aureus", "DM DOKURO - Interstellar Stomper"),
            S("PlaguebringerGoliath", "The Plaguebringer Goliath", "DM DOKURO - Fly of Beelzebub"),
            S("Ravager", "Ravager", "DM DOKURO - Open Frenzy"),
            S("AstrumDeus", "Astrum Deus", "DM DOKURO - Pest of the Cosmos"),
            S("ProfanedGuardians", "Profaned Guardians", "DM DOKURO - Unholy Ambush"),
            S("Dragonfolly", "The Dragonfolly", "DM DOKURO - Murderswarm"),
            S("Providence", "Providence, the Profaned Goddess", "DM DOKURO - Unholy Insurgency"),
            S("StormWeaver", "Storm Weaver", "DM DOKURO - Servants of the Scourge (Type W)"),
            S("CeaselessVoid", "Ceaseless Void", "DM DOKURO - Servants of the Scourge (Type V)"),
            S("Signus", "Signus, Envoy of the Devourer", "DM DOKURO - Servants of the Scourge (Type S)"),
            S("Polterghast", "Polterghast", "DM DOKURO - Feral Amalgamation"),
            S("OldDuke", "The Old Duke", "DM DOKURO - Toxic Wisdom"),
            S("DevourerOfGods", "The Devourer of Gods", "DM DOKURO - Scourge of The Universe | DM DOKURO - UNIVERSAL COLLAPSE"),
            S("Yharon", "Yharon, Dragon of Rebirth", "DM DOKURO - Infernal Catharsis (Rebirth) | DM DOKURO - Roar of the Jungle Dragon"),
            S("ExoMechs", "Exo Mechs", "PinpinNeon - Inferior Fabrications",
                "PinpinNeon - Catastrophic Fabrications | PinpinNeon - Zenith Fabrications"),
            S("SupremeCalamitas", "Supreme Witch, Calamitas", "DM DOKURO - Stained, Brutal Calamity",
                "PinpinNeon - Scars of Calamity"),
            S("PrimordialWyrm", "Primordial Wyrm", "DM DOKURO - Threats of the Ocean Floor",
                "PinpinNeon - Vocitus Terminus"),
            S("BossRush", "Boss Rush", "CDMusic - Boss Rush Tier 1 to Tier 4"),

            // Calamity add-on mods (Wrath of the Gods, Catalyst, Hunt of the Old God).
            X("AvatarOfEmptiness", "Avatar of Emptiness (Wrath of the Gods)", "ENNWAY! - PARADISE PARASITE"),
            X("NamelessDeity", "Nameless Deity of Light (Wrath of the Gods)", "Heart Plus Up! & ENNWAY! - TWISTED GARDEN"),
            X("Mars", "XG-07 Mars (Wrath of the Gods)", "Moonburn - RAMifications"),
            X("Goozma", "Goozma (Hunt of the Old God)", "Glutinous Arbitration | Viscous Desperation"),
            X("Astrageldon", "Astrageldon (Catalyst)", "DM DOKURO - Cosmic Disgust | Heart Plus Up! - Galactic Destruction"),

            S(Generic, "Any boss without a better match (modded bosses, hordes)", "Anything you like"),
            S(Escape, "Moon escape after Mithrix (optional, leave empty to keep the vanilla escape music)", "DM DOKURO - UNIVERSAL COLLAPSE"),

            // Area themes for stage music (see CalamityMusic.StageThemes.cfg for which stage uses which).
            S("Biome_Title", "Main menu", "DM DOKURO - The Tale of a Cruel World", "PinpinNeon - Sky After Rain"),
            S("Biome_SunkenSea", "Sunken Sea", "DM DOKURO - sanctuary"),
            S("Biome_SulphurousSea", "Sulphurous Sea (day)", "DM DOKURO - wasteland"),
            S("Biome_SulphurousNight", "Sulphurous Sea (night)", "Heart Plus Up! - caustic tides"),
            S("Biome_SulphurousRain", "Sulphurous Sea (rain)", "Heart Plus Up! - septication"),
            S("Biome_AcidRain", "Acid Rain", "RoverdriveX - corrosion! | PinpinNeon - nuclear monsoon"),
            S("Biome_Abyss1", "Abyss, layer 1", "Heart Plus Up! - hesitation"),
            S("Biome_Abyss2", "Abyss, layer 2", "DM DOKURO - Hadopelagic Pressure"),
            S("Biome_Abyss3", "Abyss, layer 3", "CDMusic - hydrothermophobia"),
            S("Biome_Abyss4", "Abyss, layer 4", "DM DOKURO - void"),
            S("Biome_Astral", "Astral Infection", "DM DOKURO - The Heaven-Sent Abomination"),
            S("Biome_AstralUnderground", "Astral Infection (underground)", "DM DOKURO - Treasures Within the Abomination"),
            S("Biome_BrimstoneCrags", "Brimstone Crags", "DM DOKURO - The Step Below Hell"),
            S("Biome_Lab", "Draedon's labs", "Heart Plus Up! - Engineer's Sanctum"),
            X("Biome_EternalGarden", "Eternal Garden (Wrath of the Gods)", "ENNWAY! - Edenic Whispers"),
            X("Biome_Rift", "Rift Eclipse (Wrath of the Gods)", "Moonburn - nowhere now here | Lucille Karma - Mirrortwist"),
            X("Biome_Blizzard", "Rift Eclipse blizzard (Wrath of the Gods)", "Moonburn - -273.15 | Moonburn - Svalbard"),
            X("Biome_Stars", "Wrath of the Gods sky themes", "Moonburn - Stellar Requiem | ENNWAY! & Moonburn - ascent | Moonburn - The Telescope Song"),
            X("Biome_Credits", "Ending credits (Wrath of the Gods)", "Moonburn - Wrath of the Gods"),
        };

        private static SlotDef S(string name, string boss, string suggested, string infernum = null) =>
            new SlotDef { Name = name, Boss = boss, Suggested = suggested, SuggestedInfernum = infernum };

        private static SlotDef X(string name, string boss, string suggested) =>
            new SlotDef { Name = name, Boss = boss, Suggested = suggested, ExtensionOnly = true };
    }
}
