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
        Auto,       // Calamity normally, Infernum on Monsoon / Eclipse (resolved by the director)
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
            public bool HasBiome;
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

        private readonly HashSet<string> favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> banned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public MusicLibrary(string root)
        {
            Root = root;
            LoadList(FavoritesFile, favorites);
            LoadList(BannedFile, banned);
        }

        // ------------------------------------------------------------------ favourites and bans

        private string ListDir => Path.GetDirectoryName(Path.GetFullPath(Root)) ?? Root;
        private string FavoritesFile => Path.Combine(ListDir, "favorites.txt");
        private string BannedFile => Path.Combine(ListDir, "banned.txt");
        public int BannedCount { get { lock (banned) return banned.Count; } }

        /// <summary>Path relative to the Music folder with forward slashes ("Calamity/Yharon/DM DOKURO - ....ogg").</summary>
        public string RelPath(TrackInfo t)
        {
            if (t == null) return null;
            string full = Path.GetFullPath(t.Path), root = Path.GetFullPath(Root).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
            string rel = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : Path.GetFileName(full);
            return rel.Replace('\\', '/');
        }

        public TrackInfo FindByRelPath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            foreach (var t in index.All)
                if (string.Equals(RelPath(t), rel, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        public bool IsFavorite(TrackInfo t)
        {
            if (t == null) return false;
            string key = SongKey(t);
            lock (favorites) return favorites.Contains(key);
        }

        /// <summary>Toggles a favourite (favourites are picked three times as often). Returns the new state.</summary>
        public bool ToggleFavorite(TrackInfo t)
        {
            if (t == null) return false;
            bool now;
            string rel = SongKey(t);
            lock (favorites)
            {
                now = !favorites.Remove(rel);
                if (now) favorites.Add(rel);
                SaveList(FavoritesFile, favorites);
            }
            return now;
        }

        /// <summary>Bans a song: it is never picked again (undo by editing banned.txt or Clear Banned Songs).</summary>
        public void Ban(TrackInfo t)
        {
            if (t == null) return;
            lock (banned)
            {
                banned.Add(RelPath(t));
                SaveList(BannedFile, banned);
            }
        }

        /// <summary>Bans every part of the song <paramref name="t"/> belongs to.</summary>
        public void BanSong(TrackInfo t)
        {
            if (t == null) return;
            var parts = AllPartsOf(t);
            lock (banned)
            {
                banned.Add(RelPath(t));
                if (parts != null) foreach (var p in parts) banned.Add(RelPath(p));
                SaveList(BannedFile, banned);
            }
        }

        public void ClearBans()
        {
            lock (banned)
            {
                banned.Clear();
                SaveList(BannedFile, banned);
            }
        }

        public bool IsBanned(TrackInfo t)
        {
            lock (banned) return banned.Count > 0 && banned.Contains(RelPath(t));
        }

        private static void LoadList(string file, HashSet<string> set)
        {
            try
            {
                if (!File.Exists(file)) return;
                foreach (string line in File.ReadAllLines(file))
                {
                    string l = line.Trim();
                    if (l.Length > 0 && !l.StartsWith("#")) set.Add(l.Replace('\\', '/'));
                }
            }
            catch (Exception e)
            {
                Log.Warning("Could not read " + file + ": " + e.Message);
            }
        }

        private static void SaveList(string file, HashSet<string> set)
        {
            try
            {
                var lines = new List<string> { "# One song per line, relative to the Music folder. Delete a line to undo." };
                lines.AddRange(set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                File.WriteAllLines(file, lines);
            }
            catch (Exception e)
            {
                Log.Warning("Could not write " + file + ": " + e.Message);
            }
        }

        // ------------------------------------------------------------------ multi-part songs

        /// <summary>Every part of the song <paramref name="t"/> belongs to, in order, playable or not.</summary>
        private List<TrackInfo> AllPartsOf(TrackInfo t)
        {
            if (t?.Sequence == null) return null;
            if (!index.BySlot.TryGetValue(t.Slot, out var byPack) || !byPack.TryGetValue(t.Pack, out var list)) return null;
            return list.Where(x => x.Sequence == t.Sequence).OrderBy(x => x.SequenceIndex).ToList();
        }

        /// <summary>The parts that can play: broken and banned files are left out.</summary>
        private List<TrackInfo> PartsOf(TrackInfo t)
        {
            var all = AllPartsOf(t);
            if (all == null) return null;
            lock (badFiles) all.RemoveAll(x => badFiles.Contains(x.Path));
            all.RemoveAll(IsBanned);
            return all;
        }

        /// <summary>Favourites are stored per song: every part of a multi-part song shares its first part's path.</summary>
        private string SongKey(TrackInfo t)
        {
            var all = AllPartsOf(t);
            return RelPath(all != null && all.Count > 0 ? all[0] : t);
        }

        /// <summary>How many parts the song <paramref name="t"/> belongs to has in its folder (1 for normal songs).</summary>
        public int SequenceLength(TrackInfo t)
        {
            var parts = PartsOf(t);
            return parts == null || parts.Count == 0 ? 1 : parts.Count;
        }

        /// <summary>The <paramref name="position"/>-th part (1-based, in order) of the song <paramref name="t"/> belongs to.</summary>
        public TrackInfo PartAt(TrackInfo t, int position)
        {
            var parts = PartsOf(t);
            if (parts == null || parts.Count == 0) return null;
            return parts[Math.Max(0, Math.Min(parts.Count - 1, position - 1))];
        }

        /// <summary>1-based position of <paramref name="t"/> among its song's parts.</summary>
        public int PartPosition(TrackInfo t)
        {
            var parts = PartsOf(t);
            if (parts == null) return 1;
            int i = parts.FindIndex(x => x.Path == t.Path);
            return i < 0 ? 1 : i + 1;
        }

        /// <summary>
        /// For tiered music like Boss Rush: the part numbered closest to (but not above) <paramref name="tier"/>
        /// among the multi-part songs in <paramref name="slot"/>, or any song in that folder.
        /// </summary>
        public TrackInfo PickTier(string slot, int tier, MusicMode mode)
        {
            var idx = index;
            var all = Collect(idx, new[] { slot }, PrimaryPacks(mode));
            if (all.Count == 0) all = Collect(idx, new[] { slot }, FallbackPacks(mode));
            if (all.Count == 0) return null;
            var tiered = all.Where(t => t.Sequence != null && t.SequenceIndex <= tier).OrderByDescending(t => t.SequenceIndex).ToList();
            if (tiered.Count > 0) return tiered[0];
            return all[rng.Next(all.Count)];
        }

        // ------------------------------------------------------------------ folders

        public void EnsureFolders()
        {
            bool firstRun = !Directory.Exists(Root);
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, BuildupFolder));
            foreach (string pack in new[] { PackCalamity, PackInfernum, PackExtensions })
                Directory.CreateDirectory(Path.Combine(Root, pack));

            // Area-theme folders are created on first run and when upgrading from a version without stage music.
            if (!firstRun && !Directory.Exists(Path.Combine(Root, PackCalamity, Slots.Title)))
            {
                foreach (var slot in Slots.All.Where(s => s.IsBiome))
                    Directory.CreateDirectory(Path.Combine(Root, slot.ExtensionOnly ? PackExtensions : PackCalamity, slot.Name));
                Directory.CreateDirectory(Path.Combine(Root, PackInfernum, Slots.Title));
            }

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
            sb.AppendLine("Quickest way: Settings > Mod Options > Calamity Music > Get Songs. It downloads the free");
            sb.AppendLine("Calamity / Infernum / Wrath of the Gods music mods from Steam Workshop and files every song into");
            sb.AppendLine("the right folder for you (needs Python 3). Or run tools/Get Calamity Songs.bat in the mod's folder.");
            sb.AppendLine();
            sb.AppendLine("Or drop .ogg, .mp3 or .wav files into the folders next to this file yourself:");
            sb.AppendLine();
            sb.AppendLine("  Calamity/<Boss>/     songs used in Calamity mode");
            sb.AppendLine("  Infernum/<Boss>/     songs used in Infernum mode (bosses with nothing here use the Calamity song)");
            sb.AppendLine("  Extensions/<Boss>/   songs from Calamity add-on mods (used in Calamity and Mixed mode)");
            sb.AppendLine("  Buildups/            optional custom teleporter buildups (otherwise one is synthesised live)");
            sb.AppendLine();
            sb.AppendLine("Folders starting with Biome_ hold area themes: they're the stage and menu music. Which stage");
            sb.AppendLine("uses which is in BepInEx/config/CalamityMusic.StageThemes.cfg.");
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

        private DateTime installMarker = DateTime.MinValue;
        private float markerTimer;

        /// <summary>
        /// The song installer script touches Music/.songs_installed when it finishes; reload when
        /// that happens so new songs show up without pressing anything. Checked every 2 s.
        /// </summary>
        public bool PollInstallMarker(float dt)
        {
            markerTimer -= dt;
            if (markerTimer > 0f) return false;
            markerTimer = 2f;
            try
            {
                string marker = Path.Combine(Root, ".songs_installed");
                if (!File.Exists(marker)) return false;
                var stamp = File.GetLastWriteTimeUtc(marker);
                if (installMarker == DateTime.MinValue) { installMarker = stamp; return false; }
                if (stamp == installMarker) return false;
                installMarker = stamp;
                RescanAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

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
            next.HasBiome = next.All.Any(t => Slots.IsBiome(t.Slot));
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
            result.RemoveAll(IsBanned);
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
                // Any boss theme at all (never the escape song or calm area themes).
                candidates = idx.All.Where(t => !t.Slot.Equals(Slots.Escape, StringComparison.OrdinalIgnoreCase) && !Slots.IsBiome(t.Slot) && !IsBanned(t)).ToList();
                lock (badFiles) candidates.RemoveAll(t => badFiles.Contains(t.Path));
            }
            return Choose(candidates, string.Join(",", slots), avoid);
        }

        /// <summary>
        /// Picks stage music for a scene whose theme lists <paramref name="slots"/> (area themes).
        /// Falls back to the other packs, then to any area theme except the menu and credits ones.
        /// Never returns a boss theme.
        /// </summary>
        public TrackInfo PickStage(IList<string> slots, MusicMode mode, TrackInfo avoid)
        {
            var idx = index;
            var specific = slots.Where(s => !s.Equals(Slots.AnyBiome, StringComparison.OrdinalIgnoreCase)).ToList();
            var candidates = Collect(idx, specific, PrimaryPacks(mode));
            if (candidates.Count == 0) candidates = Collect(idx, specific, FallbackPacks(mode));
            if (candidates.Count == 0)
            {
                var anyArea = idx.BySlot.Keys.Where(s => Slots.IsBiome(s)
                    && !s.Equals(Slots.Title, StringComparison.OrdinalIgnoreCase)
                    && !s.Equals(Slots.Credits, StringComparison.OrdinalIgnoreCase)).ToList();
                candidates = Collect(idx, anyArea, PrimaryPacks(mode));
                if (candidates.Count == 0) candidates = Collect(idx, anyArea, FallbackPacks(mode));
            }
            return Choose(candidates, "stage:" + string.Join(",", slots), avoid);
        }

        public bool HasStageMusic => index.HasBiome;

        private TrackInfo Choose(List<TrackInfo> candidates, string key, TrackInfo avoid)
        {
            if (candidates.Count == 0) return null;
            candidates = FirstParts(candidates);
            if (candidates.Count > 1)
            {
                lastPicked.TryGetValue(key, out string last);
                var fresh = candidates.Where(t => t.Path != last && (avoid == null || t.Path != avoid.Path)).ToList();
                if (fresh.Count > 0) candidates = fresh;
            }
            // Favourites count three times.
            var weighted = new List<TrackInfo>(candidates);
            foreach (var t in candidates)
                if (IsFavorite(t)) { weighted.Add(t); weighted.Add(t); }
            var pick = weighted[rng.Next(weighted.Count)];
            lastPicked[key] = pick.Path;
            return pick;
        }

        public TrackInfo PickEscape(MusicMode mode)
        {
            var slots = new[] { Slots.Escape };
            var c = Collect(index, slots, PrimaryPacks(mode));
            if (c.Count == 0) c = Collect(index, slots, FallbackPacks(mode));
            c = FirstParts(c);
            return c.Count == 0 ? null : c[rng.Next(c.Count)];
        }

        /// <summary>A fight always starts a multi-part song at its first available part.</summary>
        private static List<TrackInfo> FirstParts(List<TrackInfo> candidates)
        {
            var first = new Dictionary<string, int>();
            foreach (var t in candidates)
            {
                if (t.Sequence == null) continue;
                string k = t.Pack + "|" + t.Slot + "|" + t.Sequence;
                if (!first.TryGetValue(k, out int min) || t.SequenceIndex < min) first[k] = t.SequenceIndex;
            }
            if (first.Count == 0) return candidates;
            return candidates.Where(t => t.Sequence == null || first[t.Pack + "|" + t.Slot + "|" + t.Sequence] == t.SequenceIndex).ToList();
        }

        /// <summary>True for part 2+ of a multi-part song whose earlier part is in the same folder.</summary>
        public bool IsContinuation(TrackInfo track)
        {
            if (track?.Sequence == null) return false;
            if (!index.BySlot.TryGetValue(track.Slot, out var byPack) || !byPack.TryGetValue(track.Pack, out var list)) return false;
            return list.Any(t => t.Sequence == track.Sequence && t.SequenceIndex < track.SequenceIndex);
        }

        /// <summary>The part that follows <paramref name="track"/> in its sequence (same pack and folder), or null.</summary>
        public TrackInfo NextInSequence(TrackInfo track)
        {
            if (track?.Sequence == null) return null;
            if (!index.BySlot.TryGetValue(track.Slot, out var byPack) || !byPack.TryGetValue(track.Pack, out var list)) return null;
            TrackInfo next = null;
            foreach (var t in list)
            {
                if (t.Sequence != track.Sequence || t.SequenceIndex <= track.SequenceIndex) continue;
                lock (badFiles) if (badFiles.Contains(t.Path)) continue;
                if (next == null || t.SequenceIndex < next.SequenceIndex) next = t;
            }
            return next;
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
            var slots = idx.BySlot.Keys.Where(s => !s.Equals(Slots.Escape, StringComparison.OrdinalIgnoreCase) && !Slots.IsBiome(s)
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
