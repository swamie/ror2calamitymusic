using System;
using System.Globalization;
using HarmonyLib;
using RoR2;

namespace CalamityMusic.Game
{
    /// <summary>
    /// Fades the game's own (Wwise) music out while Calamity music plays and back in afterwards,
    /// by driving the music volume RTPC directly.
    ///
    /// The game reads the music volume setting back from that same RTPC, so while we hold it down
    /// the convar is patched to report (and store) the player's real value. Without this the
    /// settings menu would show 0% and could save 0% to the player's config.
    /// </summary>
    internal static class VanillaMusic
    {
        private static bool muted;
        private static string savedValue = "100";
        private static float factor = 1f;
        private static float target = 1f;
        private static float reapplyTimer;

        public static bool IsMuted => muted;

        /// <summary>The player's real music volume setting, 0..1.</summary>
        public static float SettingVolume01
        {
            get
            {
                string s = muted ? savedValue : SafeGet(AudioManager.cvVolumeMsx);
                return Parse01(s);
            }
        }

        public static float MasterVolume01 => Parse01(SafeGet(AudioManager.cvVolumeMaster));

        private static string SafeGet(AudioManager.VolumeConVar cv)
        {
            try { return cv?.GetString(); }
            catch { return null; }
        }

        public static void FadeOut() => target = 0f;
        public static void FadeIn() => target = 1f;

        public static void Update(float dt)
        {
            if (AudioManager.cvVolumeMsx == null) return;

            if (Math.Abs(factor - target) > 0.0001f)
            {
                if (!muted)
                {
                    savedValue = AudioManager.cvVolumeMsx.GetString();
                    muted = true;
                }
                float speed = target < factor ? dt / 0.6f : dt / 2.5f;
                factor = target < factor ? Math.Max(target, factor - speed) : Math.Min(target, factor + speed);
                Apply();
                if (factor >= 0.9999f && target >= 1f) Restore();
                return;
            }

            if (muted)
            {
                // Something else may have poked the RTPC; keep it pinned.
                reapplyTimer -= dt;
                if (reapplyTimer <= 0f)
                {
                    reapplyTimer = 1f;
                    Apply();
                }
            }
        }

        /// <summary>Immediately gives the game its music back (scene unload, quitting, mod disabled).</summary>
        public static void Restore()
        {
            factor = 1f;
            target = 1f;
            if (!muted) return;
            muted = false;
            try
            {
                AudioManager.cvVolumeMsx?.SetString(savedValue);
            }
            catch (Exception e)
            {
                Log.Warning("Could not restore the game's music volume: " + e.Message);
            }
        }

        private static void Apply()
        {
            var cv = AudioManager.cvVolumeMsx;
            if (cv == null || string.IsNullOrEmpty(cv.rtpcName)) return;
            float value = ParseRaw(savedValue) * factor;
            try
            {
                AkSoundEngine.SetRTPCValue(cv.rtpcName, value);
            }
            catch (Exception e)
            {
                Log.Warning("Could not set music RTPC: " + e.Message);
            }
        }

        private static float ParseRaw(string s)
        {
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
            return v;
        }

        private static float Parse01(string s)
        {
            if (string.IsNullOrEmpty(s)) return 1f;
            float v = ParseRaw(s);
            if (v > 1.0001f) v /= 100f; // the game stores 0-100
            return Math.Max(0f, Math.Min(1f, v));
        }

        [HarmonyPatch(typeof(AudioManager.VolumeConVar), nameof(AudioManager.VolumeConVar.GetString))]
        private static class GetStringPatch
        {
            private static bool Prefix(AudioManager.VolumeConVar __instance, ref string __result)
            {
                if (!muted || !ReferenceEquals(__instance, AudioManager.cvVolumeMsx)) return true;
                __result = savedValue;
                return false;
            }
        }

        [HarmonyPatch(typeof(AudioManager.VolumeConVar), nameof(AudioManager.VolumeConVar.SetString))]
        private static class SetStringPatch
        {
            private static bool Prefix(AudioManager.VolumeConVar __instance, string newValue)
            {
                if (!muted || !ReferenceEquals(__instance, AudioManager.cvVolumeMsx)) return true;
                // The player moved the music slider while we hold the music down: remember it, keep it muted.
                savedValue = newValue;
                __instance.fallbackString = newValue;
                Apply();
                return false;
            }
        }
    }
}
