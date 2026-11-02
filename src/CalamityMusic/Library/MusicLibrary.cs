using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CalamityMusic.Library
{
    internal enum MusicMode
    {
        Vanilla,
        Calamity,
        Infernum,
        Mixed,
    }

    /// <summary>
    /// Indexes the user's song folders:
    ///   Music/&lt;Pack&gt;/&lt;Slot&gt;/*.ogg|mp3|wav   (Pack = Calamity, Infernum, Extensions or any folder you make)
    ///   Music/Buildups/*                        (optional custom teleporter buildups)
    /// </summary>
    internal sealed class MusicLibrary
    {
        public const string PackCalamity = "Calamity";
        public const string PackInfernum = "Infernum";
        public const string PackExtensions = "Extensions";
        public const string BuildupFolder = "Buildups";

        private sealed class Index
        {
            public readonly Dictionary<string, Dictionary<string, List<TrackInfo>>> BySlot =
                new Dictionary<string, Dictionary<string, List<TrackInfo>>>(StringComparer.OrdinalIgnoreCase);
            public readonly List<TrackInfo> All = new List<TrackInfo>();
            public readonly List<string> Buildups = new List<string>();
        }

        private readonly object cacheLock = new object();
        private readonly Dictionary<string, KeyValuePair<DateTime, TrackInfo>> cache = new Dictionary<string, KeyValuePair<DateTime, TrackInfo>>();
        private readonly HashSet<string> badFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> lastPicked = new Dictionary<string, string>();
        private readonly System.Random rng = new System.Random();
        private volatile Index index = new Index();
        private int scanning;
        private volatile bool rescanQueued;

        public string Root { get; }
        public int TrackCount => index.All.Count;
        public int BuildupCount => index.Buildups.Count;

        public MusicLibrary(string root)
        {
            Root = root;
        }

        // ------------------------------------------------------------------ folders

        public void EnsureFolders()
        {
            bool firstRun = !Directory.Exists(Root);
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, BuildupFolder));
            foreach (string pack in new[] { PackCalamity, PackInfernum, PackExtensions })
                Directory.CreateDirectory(Path.Combine(Root, pack));

            if (firstRun)
            {
                foreach (var slot in Slots.All)
                {
                    if (slot.ExtensionOnly)
                    {
                        Directory.CreateDirectory(Path.Combine(Root, PackExtensions, slot.Name));
                        continue;
                    }
                    Directory.CreateDirectory(Path.Combine(Root, PackCalamity, slot.Name));
                    if (slot.SuggestedInfernum != null)
                        Directory.CreateDirectory(Path.Combine(Root, PackInfernum, slot.Name));
                }
                Directory.CreateDirectory(Path.Combine(Root, PackInfernum, Slots.Generic));
                Directory.CreateDirectory(Path.Combine(Root, PackExtensions, Slots.Generic));
            }

            try
            {
                File.WriteAllText(Path.Combine(Root, "HOW_TO_ADD_SONGS.txt"), BuildReadme(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Log.Warning("Could not write the song guide: " + e.Message);
            }
        }

        private string BuildReadme()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CALAMITY MUSIC - HOW TO ADD SONGS");
            sb.AppendLine("=================================");
            sb.AppendLine();
            sb.AppendLine("Quickest way: subscribe to the free Calamity Mod Music / Infernum Mode Music / Wrath of the Gods");
            sb.AppendLine("mods on Steam Workshop (open tModLoader once so they download), then run get_songs.py from");
            sb.AppendLine("https://github.com/swamie/ror2calamitymusic - it files every song into the right folder for you.");
            sb.AppendLine();
            sb.AppendLine("Or drop .ogg, .mp3 or .wav files into the folders next to this file yourself:");
            sb.AppendLine();
            sb.AppendLine("  Calamity/<Boss>/     songs used in Calamity mode");
            sb.AppendLine("  Infernum/<Boss>/     songs used in Infernum mode (bosses with nothing here use the Calamity song)");
            sb.AppendLine("  Extensions/<Boss>/   songs from Calamity add-on mods (used in Calamity and Mixed mode)");
            sb.AppendLine("  Buildups/            optional custom teleporter buildups (otherwise one is synthesised live)");
            sb.AppendLine();
            sb.AppendLine("Name files \"Artist - Title\" and Now Playing will show exactly that, e.g.");
            sb.AppendLine("  Calamity/SupremeCalamitas/DM DOKURO - Stained, Brutal Calamity.ogg");
            sb.AppendLine();
            sb.AppendLine("Put several files in one folder and the mod picks one at random each fight.");
            sb.AppendLine("You can make your own folders too; point bosses at them in CalamityMusic.BossThemes.cfg.");
            sb.AppendLine();
            sb.AppendLine("Optional tags anywhere in the filename (hidden from Now Playing):");
            sb.AppendLine("  [start=0:42]  start 42 seconds in, to skip a slow intro");
            sb.AppendLine("  [loop=1:10]   when the song loops, restart from 1:10 instead of 0:00");
            sb.AppendLine("  [gain=-3]     make this file 3 dB quieter (or + to make it louder)");
            sb.AppendLine();
            sb.AppendLine("Use \"Rescan Music Folder\" in Settings > Mod Options > Calamity Music after adding songs");
            sb.AppendLine("mid-game (the folder is also rescanned at the start of every stage).");
            sb.AppendLine();
            sb.AppendLine("SONG CHECKLIST (folder -> Calamity boss -> suggested track)");
            sb.AppendLine("----------------------------------------------------------");
            foreach (var s in Slots.All)
            {
                string pack = s.ExtensionOnly ? PackExtensions : PackCalamity;
                sb.AppendLine(pack + "/" + s.Name + "   (" + s.Boss + ")");
                sb.AppendLine("    " + s.Suggested);
                if (s.SuggestedInfernum != null)
                    sb.AppendLine("    Infernum/" + s.Name + ": " + s.SuggestedInfernum);
            }
            sb.AppendLine();
            sb.AppendLine("Which Risk of Rain 2 boss uses which folder is listed in BepInEx/config/CalamityMusic.BossThemes.cfg.");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ scanning

        public void RescanAsync()
        {
            rescanQueued = true;
            if (Interlocked.Exchange(ref scanning, 1) == 1) return; // the running scan will pick the request up
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    while (rescanQueued)
                    {
                        rescanQueued = false;
                        Rescan();
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Music folder scan failed: " + e);
                }
                finally
                {
                    Interlocked.Exchange(ref scanning, 0);
                }
            });
        }

        public void Rescan()
        {
            var next = new Index();
            if (Directory.Exists(Root))
            {
                foreach (string dir in Directory.GetDirectories(Root))
                {
                    string pack = Path.GetFileName(dir);
                    if (pack.Equals(BuildupFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        next.Buildups.AddRange(SongFiles(dir, true));
                        continue;
                    }
                    // Loose files in a pack folder count as Generic; slot folders may have sub-folders.
                    foreach (string file in SongFiles(dir, false))
                        Add(next, file, pack, Slots.Generic);
                    foreach (string slotDir in Directory.GetDirectories(dir))
                    {
                        string slot = Path.GetFileName(slotDir);
                        foreach (string file in SongFiles(slotDir, true))
                            Add(next, file, pack, slot);
                    }
                }
            }
            index = next;
            lock (badFiles) badFiles.Clear(); // give files that failed (locked, mid-download) another chance
            Log.Info($"Music library: {next.All.Count} song(s) in {next.BySlot.Count} theme folder(s), {next.Buildups.Count} custom buildup(s).");
        }

        private static IEnumerable<string> SongFiles(string dir, bool recursive)
        {
            try
            {
                return Directory.GetFiles(dir, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(AudioDecoderFacade.IsSupported)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception e)
            {
                Log.Warning("Could not read " + dir + ": " + e.Message);
                return new string[0];
            }
        }

        private void Add(Index idx, string file, string pack, string slot)
        {
            TrackInfo info;
            DateTime stamp = File.GetLastWriteTimeUtc(file);
            string key = pack + "|" + slot + "|" + file;
            lock (cacheLock)
            {
                if (!cache.TryGetValue(key, out var hit) || hit.Key != stamp)
                {
                    hit = new KeyValuePair<DateTime, TrackInfo>(stamp, TrackInfo.FromFile(file, pack, slot));
                    cache[key] = hit;
                }
                info = hit.Value;
            }

            if (!idx.BySlot.TryGetValue(slot, out var packs))
                idx.BySlot[slot] = packs = new Dictionary<string, List<TrackInfo>>(StringComparer.OrdinalIgnoreCase);
            if (!packs.TryGetValue(pack, out var list))
                packs[pack] = list = new List<TrackInfo>();
            list.Add(info);
            idx.All.Add(info);
        }

        // ------------------------------------------------------------------ picking

        public void MarkBad(TrackInfo t)
        {
            lock (badFiles) badFiles.Add(t.Path);
        }

        public bool HasSlot(string slot, MusicMode mode)
        {
            return Collect(index, new[] { slot }, PrimaryPacks(mode)).Count > 0
                || Collect(index, new[] { slot }, FallbackPacks(mode)).Count > 0;
        }

        private static string[] PrimaryPacks(MusicMode mode)
        {
            switch (mode)
            {
                case MusicMode.Infernum: return new[] { PackInfernum };
                case MusicMode.Mixed: return null; // every pack
                default: return new[] { PackCalamity, PackExtensions };
            }
        }

        private static string[] FallbackPacks(MusicMode mode)
        {
            switch (mode)
            {
                case MusicMode.Infernum: return new[] { PackCalamity, PackExtensions };
                case MusicMode.Mixed: return new string[0];
                default: return new[] { PackInfernum };
            }
        }

        private List<TrackInfo> Collect(Index idx, IEnumerable<string> slots, string[] packs)
        {
            var result = new List<TrackInfo>();
            if (packs != null && packs.Length == 0) return result;
            foreach (string slot in slots)
            {
                if (!idx.BySlot.TryGetValue(slot, out var byPack)) continue;
                foreach (var kv in byPack)
                {
                    bool known = kv.Key.Equals(PackCalamity, StringComparison.OrdinalIgnoreCase)
                        || kv.Key.Equals(PackInfernum, StringComparison.OrdinalIgnoreCase)
                        || kv.Key.Equals(PackExtensions, StringComparison.OrdinalIgnoreCase);
                    // Packs the user invented themselves count as extensions.
                    string effective = known ? kv.Key : PackExtensions;
                    if (packs == null || packs.Any(p => p.Equals(effective, StringComparison.OrdinalIgnoreCase)))
                        result.AddRange(kv.Value);
                }
            }
            lock (badFiles) result.RemoveAll(t => badFiles.Contains(t.Path));
            return result;
        }

        /// <summary>
        /// Picks a song for a boss whose theme lists <paramref name="slots"/>. Falls back to the other
        /// packs, then to the Generic folder, then (optionally) to any song at all.
        /// </summary>
        public TrackInfo Pick(IList<string> slots, MusicMode mode, TrackInfo avoid, bool anySongFallback, out bool usedFallback)
        {
            var idx = index;
            usedFallback = false;
            var candidates = Collect(idx, slots, PrimaryPacks(mode));
            if (candidates.Count == 0) candidates = Collect(idx, slots, FallbackPacks(mode));
            if (candidates.Count == 0)
            {
                usedFallback = true;
                candidates = Collect(idx, new[] { Slots.Generic }, null);
            }
            if (candidates.Count == 0 && anySongFallback)
            {
                candidates = idx.All.Where(t => !t.Slot.Equals(Slots.Escape, StringComparison.OrdinalIgnoreCase)).ToList();
                lock (badFiles) candidates.RemoveAll(t => badFiles.Contains(t.Path));
            }
            if (candidates.Count == 0) return null;

            string key = string.Join(",", slots);
            if (candidates.Count > 1)
            {
                lastPicked.TryGetValue(key, out string last);
                var fresh = candidates.Where(t => t.Path != last && (avoid == null || t.Path != avoid.Path)).ToList();
                if (fresh.Count > 0) candidates = fresh;
            }
            var pick = candidates[rng.Next(candidates.Count)];
            lastPicked[key] = pick.Path;
            return pick;
        }

        public TrackInfo PickEscape(MusicMode mode)
        {
            var slots = new[] { Slots.Escape };
            var c = Collect(index, slots, PrimaryPacks(mode));
            if (c.Count == 0) c = Collect(index, slots, FallbackPacks(mode));
            return c.Count == 0 ? null : c[rng.Next(c.Count)];
        }

        public string PickBuildup()
        {
            var b = index.Buildups;
            return b.Count == 0 ? null : b[rng.Next(b.Count)];
        }

        /// <summary>A random slot that has songs, for the settings-menu preview.</summary>
        public string RandomFilledSlot(MusicMode mode)
        {
            var idx = index;
            var slots = idx.BySlot.Keys.Where(s => !s.Equals(Slots.Escape, StringComparison.OrdinalIgnoreCase)
                                                    && Collect(idx, new[] { s }, PrimaryPacks(mode)).Count > 0).ToList();
            if (slots.Count == 0) slots = idx.BySlot.Keys.ToList();
            return slots.Count == 0 ? null : slots[rng.Next(slots.Count)];
        }
    }

    internal static class AudioDecoderFacade
    {
        public static bool IsSupported(string path) => Audio.AudioDecoder.IsSupported(path);
    }
}
