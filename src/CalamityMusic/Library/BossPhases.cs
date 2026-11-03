using System;

namespace CalamityMusic.Library
{
    /// <summary>
    /// Which phase a multi-phase boss fight is in, and which part of a multi-part song goes with
    /// it. Kept free of game types so it can be tested on its own.
    /// </summary>
    internal static class BossPhases
    {
        public const int MithrixPhases = 4;
        private const string Voidling = "MiniVoidRaidCrabBodyPhase";
        private const string FalseSon = "FalseSonBossBody";

        /// <summary>
        /// Phase from the boss body alone: Voidling's three bodies, False Son's three forms, and
        /// Mithrix's hurt form (always his last phase). Plain Mithrix needs the encounter state.
        /// </summary>
        public static bool FromBodyName(string body, out int phase, out int count)
        {
            phase = count = 0;
            if (string.IsNullOrEmpty(body)) return false;
            if (body.StartsWith("BrotherHurt", StringComparison.OrdinalIgnoreCase))
            {
                phase = count = MithrixPhases;
                return true;
            }
            if (body.StartsWith(Voidling, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(body.Substring(Voidling.Length), out int v))
            {
                count = 3;
                phase = Math.Max(1, Math.Min(3, v));
                return true;
            }
            if (body.StartsWith(FalseSon, StringComparison.OrdinalIgnoreCase))
            {
                count = 3;
                string rest = body.Substring(FalseSon.Length);
                phase = rest.IndexOf("Broken", StringComparison.OrdinalIgnoreCase) >= 0 ? 3
                      : rest.IndexOf("LunarShard", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
                return true;
            }
            return false;
        }

        /// <summary>Mithrix encounter phase from the moon's encounter state ("Phase1".."Phase4"), or 0.</summary>
        public static int FromBrotherState(string stateTypeName)
        {
            switch (stateTypeName)
            {
                case "Phase1": return 1;
                case "Phase2": return 2;
                case "Phase3": return 3;
                case "Phase4": return 4;
                default: return 0;
            }
        }

        /// <summary>
        /// Spreads a fight's phases over a song's parts: first phase -> first part, last phase -> last
        /// part, the ones in between evenly (Mithrix's 4 phases over Supreme Calamitas' 3 parts: 1, 2, 2, 3).
        /// </summary>
        public static int PartForPhase(int phase, int phases, int parts)
        {
            if (parts <= 1) return 1;
            if (phases <= 1) return 1;
            phase = Math.Max(1, Math.Min(phases, phase));
            double x = (phase - 1) * (parts - 1) / (double)(phases - 1);
            return Math.Max(1, Math.Min(parts, (int)Math.Floor(x + 0.5) + 1));
        }

        /// <summary>One name for every form of a boss, so its title card shows once per fight.</summary>
        public static string Family(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            if (body.StartsWith("BrotherHurt", StringComparison.OrdinalIgnoreCase)) return "BrotherBody";
            if (body.StartsWith(Voidling, StringComparison.OrdinalIgnoreCase)) return "MiniVoidRaidCrabBody";
            if (body.StartsWith(FalseSon, StringComparison.OrdinalIgnoreCase)) return FalseSon;
            return body;
        }

        /// <summary>Simulacrum wave to Boss Rush tier: a tier every 10 waves, 1 to 5.</summary>
        public static int BossRushTier(int waveIndex) => Math.Max(1, Math.Min(5, 1 + Math.Max(0, waveIndex - 1) / 10));
    }
}
