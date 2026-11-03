using System.Collections.Generic;
using HarmonyLib;
using RoR2;

namespace CalamityMusic.Game
{
    /// <summary>
    /// Keeps a list of every BossGroup in the scene (teleporter bosses, Mithrix, Voidling, False Son,
    /// Aurelionite, Starstorm 2 Nemesis/Empyrean fights, modded bosses...). Their member lists are
    /// networked, so this works the same for the host and for clients.
    /// </summary>
    [HarmonyPatch]
    internal static class BossGroupTracker
    {
        public static readonly List<BossGroup> Groups = new List<BossGroup>();

        /// <summary>False if the Awake patch didn't apply; the director then scans the scene instead.</summary>
        public static bool Patched = true;
        private static float nextScan;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BossGroup), nameof(BossGroup.Awake))]
        private static void OnAwake(BossGroup __instance)
        {
            if (!Groups.Contains(__instance)) Groups.Add(__instance);
        }

        public static void Prune()
        {
            Groups.RemoveAll(g => !g);
        }

        /// <summary>Fallback when the patch is missing: look the groups up directly, at most once a second.</summary>
        public static void RefreshIfUnpatched(float now)
        {
            if (Patched || now < nextScan) return;
            nextScan = now + 1f;
            Groups.Clear();
            Groups.AddRange(UnityEngine.Object.FindObjectsOfType<BossGroup>());
        }
    }
}
