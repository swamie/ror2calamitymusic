using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CalamityMusic.Library;

namespace CalamityMusic.UI
{
    internal enum ParticleKind
    {
        Sparkles,   // twinkling four-point stars (default Infernum look)
        Embers,     // glowing sparks rising and flickering
        Bubbles,    // rings drifting upward
        Snow,       // flakes falling and spinning
        Stars,      // twinkling stars plus the odd shooting star
        Wisps,      // big soft ghostly lights drifting sideways
        Drips,      // droplets falling from the text
        Pixels,     // blinking square pixels (machines)
        Petals,     // soft petals falling and swaying
        Void,       // dark motes pulled into the centre
        Sparks,     // fast streaks shooting outward (storms)
    }

    /// <summary>A visual theme for the Now Playing banner. Colours are hex strings so this file has no Unity dependency.</summary>
    internal sealed class BannerStyle
    {
        public string Name;
        public string Fill, Outline, Glow, Accent, Backdrop;
        public ParticleKind Particles;
        public float Rate = 12f;       // particles per second at rest; bass adds more
        public bool Glitch;            // RGB-split jitter (Exo Mechs, labs)
        public bool Lightning;         // white flashes on the beat
        public bool Flicker;           // fire-like glow flicker
    }

    /// <summary>Picks a banner style from the song's folder (its boss or area theme).</summary>
    internal static class BannerStyles
    {
        private static BannerStyle S(string name, string fill, string outline, string glow, string accent, string backdrop,
            ParticleKind particles, float rate = 12f, bool glitch = false, bool lightning = false, bool flicker = false) =>
            new BannerStyle
            {
                Name = name, Fill = fill, Outline = outline, Glow = glow, Accent = accent, Backdrop = backdrop,
                Particles = particles, Rate = rate, Glitch = glitch, Lightning = lightning, Flicker = flicker,
            };

        // The default is the look from the Infernum music display: white, royal blue, teal glow.
        public static readonly BannerStyle Default = S("Infernum", "#F4F6FF", "#2A42BE", "#3CF0C8", "#9AF7FF", "#05081A", ParticleKind.Sparkles, 9f);
        public static readonly BannerStyle Brimstone = S("Brimstone", "#FFEDE6", "#8C0F14", "#FF4A1F", "#FFB03A", "#1A0303", ParticleKind.Embers, 22f, flicker: true);
        public static readonly BannerStyle HolyFire = S("Holy Fire", "#FFF8E6", "#A04E00", "#FFC23A", "#FFF1A8", "#1A0E00", ParticleKind.Embers, 18f, flicker: true);
        public static readonly BannerStyle Cosmic = S("Cosmic", "#F4ECFF", "#3B1670", "#C04BFF", "#5CF2FF", "#07021A", ParticleKind.Void, 16f);
        public static readonly BannerStyle Storm = S("Storm", "#EEF1FF", "#3A2A9E", "#8FA8FF", "#FFFFFF", "#050616", ParticleKind.Sparks, 10f, lightning: true);
        public static readonly BannerStyle Tech = S("Tech", "#EAFFFF", "#0E3A4A", "#38E8FF", "#FF3B4E", "#010A0E", ParticleKind.Pixels, 20f, glitch: true);
        public static readonly BannerStyle Ocean = S("Ocean", "#E9FBFF", "#0B3E7A", "#3FD8FF", "#B6F3FF", "#010C1A", ParticleKind.Bubbles, 12f);
        public static readonly BannerStyle Toxic = S("Toxic", "#F2FFE6", "#2E5410", "#9BFF3A", "#E6FF6A", "#071002", ParticleKind.Drips, 10f);
        public static readonly BannerStyle Astral = S("Astral", "#FFF0F8", "#4A1F6B", "#FF7AD9", "#FFB070", "#12041A", ParticleKind.Stars, 14f);
        public static readonly BannerStyle Spectral = S("Spectral", "#EAFFFB", "#155E63", "#6CFFE1", "#C9FFF4", "#011012", ParticleKind.Wisps, 6f);
        public static readonly BannerStyle Frost = S("Frost", "#F6FCFF", "#2B5C8A", "#BDEBFF", "#FFFFFF", "#030A12", ParticleKind.Snow, 16f);
        public static readonly BannerStyle Blood = S("Blood", "#FFEAEA", "#5A0E1A", "#FF3B5C", "#FF9AA8", "#140205", ParticleKind.Drips, 9f);
        public static readonly BannerStyle Fungal = S("Fungal", "#EAF5FF", "#23469E", "#6FA8FF", "#B9F0FF", "#020717", ParticleKind.Wisps, 8f);
        public static readonly BannerStyle Divine = S("Divine", "#FFFFFF", "#6B5A9E", "#FFF2B0", "#FFC9E8", "#0C0A14", ParticleKind.Petals, 10f);
        public static readonly BannerStyle Rift = S("Rift", "#FFE8E8", "#3A0A10", "#FF2A3A", "#FFFFFF", "#0A0002", ParticleKind.Void, 14f, glitch: true);
        public static readonly BannerStyle Abyss = S("Abyss", "#E6F0FF", "#0A1E4A", "#3C6BFF", "#7FE8FF", "#00030C", ParticleKind.Bubbles, 6f);
        public static readonly BannerStyle BossRush = S("Boss Rush", "#FFFFFF", "#7A0A2A", "#FF3355", "#FFD23A", "#120008", ParticleKind.Sparks, 14f, lightning: true);

        public static readonly BannerStyle[] All =
            { Default, Brimstone, HolyFire, Cosmic, Storm, Tech, Ocean, Toxic, Astral, Spectral, Frost, Blood, Fungal, Divine, Rift, Abyss, BossRush };

        private static readonly Dictionary<string, BannerStyle> BySlot = new Dictionary<string, BannerStyle>(StringComparer.OrdinalIgnoreCase)
        {
            ["SupremeCalamitas"] = Brimstone, ["CalamitasClone"] = Brimstone, ["BrimstoneElemental"] = Brimstone, ["Biome_BrimstoneCrags"] = Brimstone,
            ["Yharon"] = HolyFire, ["ProfanedGuardians"] = HolyFire, ["Providence"] = HolyFire,
            ["DevourerOfGods"] = Cosmic, ["Signus"] = Cosmic, ["CeaselessVoid"] = Cosmic, ["AvatarOfEmptiness"] = Cosmic, ["MoonEscape"] = Cosmic, ["Biome_Abyss4"] = Cosmic,
            ["StormWeaver"] = Storm, ["Dragonfolly"] = Storm,
            ["ExoMechs"] = Tech, ["Mars"] = Tech, ["Biome_Lab"] = Tech,
            ["LeviathanAnahita"] = Ocean, ["AquaticScourge"] = Ocean, ["Biome_SunkenSea"] = Ocean, ["Biome_Abyss1"] = Ocean,
            ["PrimordialWyrm"] = Abyss, ["Biome_Abyss2"] = Abyss, ["Biome_Abyss3"] = Abyss,
            ["OldDuke"] = Toxic, ["PlaguebringerGoliath"] = Toxic, ["Biome_SulphurousSea"] = Toxic, ["Biome_SulphurousNight"] = Toxic,
            ["Biome_SulphurousRain"] = Toxic, ["Biome_AcidRain"] = Toxic,
            ["AstrumDeus"] = Astral, ["AstrumAureus"] = Astral, ["Astrageldon"] = Astral, ["Biome_Astral"] = Astral,
            ["Biome_AstralUnderground"] = Astral, ["Biome_Stars"] = Astral,
            ["Polterghast"] = Spectral,
            ["Cryogen"] = Frost, ["Biome_Blizzard"] = Frost,
            ["Ravager"] = Blood, ["Perforators"] = Blood, ["HiveMind"] = Blood,
            ["Crabulon"] = Fungal, ["SlimeGod"] = Fungal, ["DesertScourge"] = Fungal, ["Goozma"] = Fungal,
            ["NamelessDeity"] = Divine, ["Biome_EternalGarden"] = Divine, ["Biome_Credits"] = Divine,
            ["Biome_Rift"] = Rift,
            [Slots.BossRush] = BossRush,
        };

        private static readonly (string[] words, BannerStyle style)[] Keywords =
        {
            (new[] { "calamit", "brimstone", "fire", "flame", "hell", "infern", "lava" }, Brimstone),
            (new[] { "holy", "profaned", "sun", "gold", "yharon", "dragon" }, HolyFire),
            (new[] { "void", "devour", "cosmic", "space", "universe" }, Cosmic),
            (new[] { "storm", "thunder", "lightning", "electric" }, Storm),
            (new[] { "exo", "mech", "lab", "machine", "draedon", "robot" }, Tech),
            (new[] { "sea", "ocean", "water", "aquatic", "leviathan", "sunken" }, Ocean),
            (new[] { "abyss", "deep" }, Abyss),
            (new[] { "acid", "toxic", "sulph", "sulf", "plague" }, Toxic),
            (new[] { "astral", "astrum", "star" }, Astral),
            (new[] { "ghost", "spirit", "polter", "soul" }, Spectral),
            (new[] { "ice", "frost", "snow", "cryo", "blizzard", "winter" }, Frost),
            (new[] { "blood", "flesh", "crimson" }, Blood),
            (new[] { "garden", "divine", "nameless", "heaven" }, Divine),
            (new[] { "rift", "eclipse" }, Rift),
        };

        public static BannerStyle For(TrackInfo track) => track == null ? Default : Find(track.Slot, track.Title);

        public static BannerStyle ForSlot(string slot) => Find(slot, null);

        private static BannerStyle Find(string slot, string title)
        {
            if (slot != null && BySlot.TryGetValue(slot, out var style)) return style;
            string hay = ((slot ?? "") + " " + (title ?? "")).ToLowerInvariant();
            foreach (var k in Keywords)
                if (k.words.Any(w => hay.Contains(w))) return k.style;
            return Default;
        }

        /// <summary>The boss or area a song belongs to, for the line under the title ("Supreme Witch, Calamitas").</summary>
        public static string ThemeName(TrackInfo track)
        {
            if (track?.Slot == null || track.Slot.Equals(Slots.Generic, StringComparison.OrdinalIgnoreCase)) return null;
            if (track.Slot.Equals(Slots.Escape, StringComparison.OrdinalIgnoreCase)) return "Escape";
            var def = Slots.All.FirstOrDefault(s => s.Name.Equals(track.Slot, StringComparison.OrdinalIgnoreCase));
            string name = def?.Boss ?? Humanize(track.Slot);
            int paren = name.IndexOf(" (", StringComparison.Ordinal); // "Avatar of Emptiness (Wrath of the Gods)" -> "Avatar of Emptiness"
            return paren > 0 ? name.Substring(0, paren) : name;
        }

        private static string Humanize(string slot)
        {
            string s = slot.StartsWith(Slots.BiomePrefix, StringComparison.OrdinalIgnoreCase) ? slot.Substring(Slots.BiomePrefix.Length) : slot;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(s[i] == '_' ? ' ' : s[i]);
            }
            return sb.ToString().Trim();
        }
    }
}
