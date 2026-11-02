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

        public sealed class SlotDef
        {
            public string Name;
            public string Boss;
            public string Suggested;          // base Calamity / music mod
            public string SuggestedInfernum;  // Infernum override, if one exists
            public bool ExtensionOnly;
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

            S(Generic, "Any boss without a better match (modded bosses, hordes)", "Anything you like, e.g. ENNWAY! - Edenic Whispers"),
            S(Escape, "Moon escape after Mithrix (optional, leave empty to keep the vanilla escape music)", "DM DOKURO - UNIVERSAL COLLAPSE"),
        };

        private static SlotDef S(string name, string boss, string suggested, string infernum = null) =>
            new SlotDef { Name = name, Boss = boss, Suggested = suggested, SuggestedInfernum = infernum };

        private static SlotDef X(string name, string boss, string suggested) =>
            new SlotDef { Name = name, Boss = boss, Suggested = suggested, ExtensionOnly = true };
    }
}
