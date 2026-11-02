using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CalamityMusic.Library
{
    /// <summary>
    /// One song file plus the metadata the mod needs. The display name comes from the filename
    /// ("Artist - Title.ogg"), falling back to embedded tags. Optional tags in square brackets
    /// tweak playback and are hidden from the display:
    ///   [start=1:05]  begin playback 65 seconds in (skip a long intro)
    ///   [loop=0:12]   when the song loops, jump back to 0:12 instead of the very start
    ///   [gain=-3]     make this one file quieter/louder in dB
    /// </summary>
    internal sealed class TrackInfo
    {
        private static readonly Regex TagRegex = new Regex(@"\[(start|loop|gain)\s*=\s*([^\]]+)\]", RegexOptions.IgnoreCase);
        private static readonly Regex JunkRegex = new Regex(@"\s*[\(\[](official\s*(audio|video|music\s*video)?|hq|hd|lyrics?|audio|full\s*song|4k|1080p)[\)\]]", RegexOptions.IgnoreCase);

        public string Path { get; }
        public string Pack { get; }
        public string Slot { get; }
        public string Artist { get; }
        public string Title { get; }
        public double StartSeconds { get; }
        public double LoopStartSeconds { get; }
        public float GainDb { get; }

        public string Display => string.IsNullOrEmpty(Artist) ? Title : Artist + " - " + Title;

        private TrackInfo(string path, string pack, string slot, string artist, string title, double start, double loop, float gain)
        {
            Path = path;
            Pack = pack;
            Slot = slot;
            Artist = artist;
            Title = title;
            StartSeconds = start;
            LoopStartSeconds = loop;
            GainDb = gain;
        }

        public static TrackInfo FromFile(string path, string pack, string slot)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            double start = 0, loop = -1;
            float gain = 0;
            foreach (Match m in TagRegex.Matches(name))
            {
                string key = m.Groups[1].Value.ToLowerInvariant();
                string val = m.Groups[2].Value.Trim();
                switch (key)
                {
                    case "start": start = ParseTime(val); break;
                    case "loop": loop = ParseTime(val); break;
                    case "gain":
                        float.TryParse(val.Replace("dB", "").Replace("db", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out gain);
                        gain = Math.Max(-24f, Math.Min(12f, gain));
                        break;
                }
            }
            name = TagRegex.Replace(name, "");
            name = JunkRegex.Replace(name, "");
            name = name.Replace('_', ' ').Trim();
            if (loop < 0) loop = 0;

            string artist = null, title = name;
            int dash = name.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                artist = name.Substring(0, dash).Trim();
                title = name.Substring(dash + 3).Trim();
            }
            else
            {
                // No "Artist - Title" in the filename: try the file's own tags.
                try
                {
                    TagReader.TryRead(path, out string tagArtist, out string tagTitle);
                    if (!string.IsNullOrWhiteSpace(tagTitle))
                    {
                        title = tagTitle.Trim();
                        artist = string.IsNullOrWhiteSpace(tagArtist) ? null : tagArtist.Trim();
                    }
                }
                catch
                {
                    // Tags are a nicety; ignore broken ones.
                }
            }
            if (string.IsNullOrWhiteSpace(title)) title = System.IO.Path.GetFileNameWithoutExtension(path);

            return new TrackInfo(path, pack, slot, artist, title, start, loop, gain);
        }

        private static double ParseTime(string s)
        {
            s = s.Trim();
            double total = 0;
            foreach (string part in s.Split(':'))
            {
                double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
                total = total * 60 + v;
            }
            return Math.Max(0, total);
        }

        public override string ToString() => Display + " [" + Pack + "/" + Slot + "]";
    }

    /// <summary>Reads artist/title from ID3v2 (mp3) or Vorbis comments (ogg). Best effort only.</summary>
    internal static class TagReader
    {
        public static void TryRead(string path, out string artist, out string title)
        {
            artist = null;
            title = null;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp3") ReadId3(path, ref artist, ref title);
            else if (ext == ".ogg") ReadVorbis(path, ref artist, ref title);
        }

        private static void ReadVorbis(string path, ref string artist, ref string title)
        {
            using (var reader = new NVorbis.VorbisReader(path))
            {
                foreach (string c in reader.Comments ?? new string[0])
                {
                    int eq = c.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = c.Substring(0, eq).ToUpperInvariant();
                    string val = c.Substring(eq + 1);
                    if (key == "ARTIST" && artist == null) artist = val;
                    else if (key == "TITLE" && title == null) title = val;
                }
            }
        }

        private static void ReadId3(string path, ref string artist, ref string title)
        {
            using (var fs = File.OpenRead(path))
            {
                var header = new byte[10];
                if (fs.Read(header, 0, 10) < 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3') return;
                int version = header[3];
                int size = (header[6] & 0x7F) << 21 | (header[7] & 0x7F) << 14 | (header[8] & 0x7F) << 7 | (header[9] & 0x7F);
                size = Math.Min(size, 1 << 20);
                var tag = new byte[size];
                int read = fs.Read(tag, 0, size);
                int pos = 0;
                int idLen = version == 2 ? 3 : 4;
                int hdrLen = version == 2 ? 6 : 10;
                while (pos + hdrLen <= read)
                {
                    string id = Encoding.ASCII.GetString(tag, pos, idLen);
                    if (id[0] == '\0') break;
                    int fsize;
                    if (version == 2) fsize = tag[pos + 3] << 16 | tag[pos + 4] << 8 | tag[pos + 5];
                    else if (version == 4) fsize = (tag[pos + 4] & 0x7F) << 21 | (tag[pos + 5] & 0x7F) << 14 | (tag[pos + 6] & 0x7F) << 7 | (tag[pos + 7] & 0x7F);
                    else fsize = tag[pos + 4] << 24 | tag[pos + 5] << 16 | tag[pos + 6] << 8 | tag[pos + 7];
                    int start = pos + hdrLen;
                    if (fsize <= 0 || start + fsize > read) break;
                    if (id == "TIT2" || id == "TT2") title = title ?? DecodeText(tag, start, fsize);
                    else if (id == "TPE1" || id == "TP1") artist = artist ?? DecodeText(tag, start, fsize);
                    pos = start + fsize;
                }
            }
        }

        private static string DecodeText(byte[] b, int start, int len)
        {
            if (len < 2) return null;
            byte enc = b[start];
            Encoding e;
            switch (enc)
            {
                case 1: e = Encoding.Unicode; break;          // UTF-16 with BOM
                case 2: e = Encoding.BigEndianUnicode; break; // UTF-16BE
                case 3: e = Encoding.UTF8; break;
                default: e = Encoding.GetEncoding("ISO-8859-1"); break;
            }
            int s = start + 1, l = len - 1;
            if (enc == 1 && l >= 2)
            {
                if (b[s] == 0xFE && b[s + 1] == 0xFF) e = Encoding.BigEndianUnicode;
                if ((b[s] == 0xFF && b[s + 1] == 0xFE) || (b[s] == 0xFE && b[s + 1] == 0xFF)) { s += 2; l -= 2; }
            }
            return e.GetString(b, s, l).TrimEnd('\0').Trim();
        }
    }
}
