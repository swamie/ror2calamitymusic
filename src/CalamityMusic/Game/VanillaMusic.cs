using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using RoR2;

namespace CalamityMusic.Game
{
    /// <summary>
    /// Fades the game's own (Wwise) music out while Calamity music plays and back in afterwards,
    /// by driving the music volume RTPC(s) directly.
    ///
    /// The game reads the music volume setting back from that same RTPC, so while we hold it down
    /// the convar is patched to report (and store) the player's real value. Without this the
    /// settings menu would show 0% and could save 0% to the player's config. If those patches
    /// didn't apply (a future game update), <see cref="Enabled"/> is false and nothing is muted.
    /// </summary>
    internal static class VanillaMusic
    {
        private sealed class Held
        {
            public AudioManager.VolumeConVar ConVar;
            public string Saved;
        }

        public static bool Enabled = true;

        private static readonly List<Held> held = new List<Held>();
        private static bool muted;
        private static float factor = 1f;
        private static float target = 1f;
        private static float reapplyTimer;

        public static bool IsMuted => muted;

        /// <summary>The music convars: the Music volume slider and its parent bus, when the game has one.</summary>
        private static IEnumerable<AudioManager.VolumeConVar> MusicConVars()
        {
            if (AudioManager.cvVolumeMsx != null) yield return AudioManager.cvVolumeMsx;
            if (AudioManager.cvVolumeParentMsx != null && !ReferenceEquals(AudioManager.cvVolumeParentMsx, AudioManager.cvVolumeMsx))
                yield return AudioManager.cvVolumeParentMsx;
        }

        private static Held Find(AudioManager.VolumeConVar cv)
        {
            foreach (var h in held)
                if (ReferenceEquals(h.ConVar, cv)) return h;
            return null;
        }

        /// <summary>The player's real music volume setting, 0..1.</summary>
        public static float SettingVolume01
        {
            get
            {
                float v = 1f;
                foreach (var cv in MusicConVars())
                {
                    var h = muted ? Find(cv) : null;
                    v *= Parse01(h != null ? h.Saved : SafeGet(cv));
                }
                return v;
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
            if (!Enabled)
            {
                if (muted) Restore();
                return;
            }

            if (Math.Abs(factor - target) > 0.0001f)
            {
                if (!muted)
                {
                    held.Clear();
                    foreach (var cv in MusicConVars())
                        held.Add(new Held { ConVar = cv, Saved = SafeGet(cv) ?? "100" });
                    if (held.Count == 0) return;
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
            foreach (var h in held)
            {
                try
                {
                    h.ConVar.SetString(h.Saved);
                }
                catch (Exception e)
                {
                    Log.Warning("Could not restore the game's music volume: " + e.Message);
                }
            }
            held.Clear();
        }

        private static void Apply()
        {
            foreach (var h in held)
            {
                if (string.IsNullOrEmpty(h.ConVar.rtpcName)) continue;
                try
                {
                    AkSoundEngine.SetRTPCValue(h.ConVar.rtpcName, ParseRaw(h.Saved) * factor);
                }
                catch (Exception e)
                {
                    Log.Warning("Could not set music RTPC: " + e.Message);
                }
            }
        }

        private static float ParseRaw(string s)
        {
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
            return v;
        }

        /// <summary>The game stores volumes as 0-100.</summary>
        private static float Parse01(string s)
        {
            if (string.IsNullOrEmpty(s)) return 1f;
            return Math.Max(0f, Math.Min(1f, ParseRaw(s) / 100f));
        }

        [HarmonyPatch(typeof(AudioManager.VolumeConVar), nameof(AudioManager.VolumeConVar.GetString))]
        internal static class GetStringPatch
        {
            private static bool Prefix(AudioManager.VolumeConVar __instance, ref string __result)
            {
                if (!muted) return true;
                var h = Find(__instance);
                if (h == null) return true;
                __result = h.Saved;
                return false;
            }
        }

        [HarmonyPatch(typeof(AudioManager.VolumeConVar), nameof(AudioManager.VolumeConVar.SetString))]
        internal static class SetStringPatch
        {
            private static bool Prefix(AudioManager.VolumeConVar __instance, string newValue)
            {
                if (!muted) return true;
                var h = Find(__instance);
                if (h == null) return true;
                // The player moved the music slider while we hold the music down: remember it, keep it muted.
                h.Saved = newValue;
                __instance.fallbackString = newValue;
                Apply();
                return false;
            }
        }
    }
}
