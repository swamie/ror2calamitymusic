using System;
using System.IO;
using NLayer;
using NVorbis;

namespace CalamityMusic.Audio
{
    /// <summary>Pull-based stream of interleaved float samples.</summary>
    internal interface ISampleSource : IDisposable
    {
        int SampleRate { get; }
        int Channels { get; }

        /// <summary>Reads up to <paramref name="count"/> floats. Returns 0 only at end of stream.</summary>
        int Read(float[] buffer, int offset, int count);
    }

    /// <summary>A decoded audio file that knows its length and can seek.</summary>
    internal interface IFileSource : ISampleSource
    {
        double LengthSeconds { get; }
        double PositionSeconds { get; set; }
    }

    internal static class AudioDecoder
    {
        public static readonly string[] SupportedExtensions = { ".ogg", ".mp3", ".wav" };

        public static bool IsSupported(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(SupportedExtensions, ext) >= 0;
        }

        /// <summary>Opens a file, sniffing the header so a wrongly named file still works.</summary>
        public static IFileSource Open(string path)
        {
            byte[] magic = new byte[4];
            using (var fs = File.OpenRead(path))
            {
                fs.Read(magic, 0, 4);
            }

            if (magic[0] == 'O' && magic[1] == 'g' && magic[2] == 'g' && magic[3] == 'S')
                return new OggSource(path);
            if (magic[0] == 'R' && magic[1] == 'I' && magic[2] == 'F' && magic[3] == 'F')
                return new WavSource(path);
            if ((magic[0] == 'I' && magic[1] == 'D' && magic[2] == '3') || (magic[0] == 0xFF && (magic[1] & 0xE0) == 0xE0))
                return new Mp3Source(path);

            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": return new OggSource(path);
                case ".wav": return new WavSource(path);
                case ".mp3": return new Mp3Source(path);
            }
            throw new NotSupportedException("Unrecognised audio format: " + Path.GetFileName(path));
        }
    }

    internal sealed class Mp3Source : IFileSource
    {
        private readonly MpegFile file;
        private readonly double length;

        public Mp3Source(string path)
        {
            file = new MpegFile(path);
            length = file.Duration.TotalSeconds;
        }

        public int SampleRate => file.SampleRate;
        public int Channels => file.Channels;
        public double LengthSeconds => length;

        public double PositionSeconds
        {
            get => file.Time.TotalSeconds;
            set => file.Time = TimeSpan.FromSeconds(Math.Max(0, Math.Min(value, Math.Max(0, length - 0.05))));
        }

        public int Read(float[] buffer, int offset, int count)
        {
            count -= count % file.Channels;
            return file.ReadSamples(buffer, offset, count);
        }

        public void Dispose() => file.Dispose();
    }

    internal sealed class OggSource : IFileSource
    {
        private readonly VorbisReader reader;
        private readonly double length;

        public OggSource(string path)
        {
            reader = new VorbisReader(File.OpenRead(path), true) { ClipSamples = false };
            length = reader.TotalTime.TotalSeconds;
        }

        public int SampleRate => reader.SampleRate;
        public int Channels => reader.Channels;
        public double LengthSeconds => length;

        public double PositionSeconds
        {
            get => reader.DecodedTime.TotalSeconds;
            set => reader.DecodedTime = TimeSpan.FromSeconds(Math.Max(0, Math.Min(value, Math.Max(0, length - 0.05))));
        }

        public int Read(float[] buffer, int offset, int count)
        {
            count -= count % reader.Channels;
            return reader.ReadSamples(buffer, offset, count);
        }

        public void Dispose() => reader.Dispose();
    }

    /// <summary>Minimal RIFF/WAVE reader: PCM 8/16/24/32 bit and IEEE float 32/64.</summary>
    internal sealed class WavSource : IFileSource
    {
        private readonly FileStream stream;
        private readonly long dataStart;
        private readonly long dataLength;
        private readonly int bytesPerSample;
        private readonly bool isFloat;
        private readonly int blockAlign;
        private byte[] raw = new byte[0];

        public int SampleRate { get; }
        public int Channels { get; }
        public double LengthSeconds { get; }

        public WavSource(string path)
        {
            stream = File.OpenRead(path);
            var br = new BinaryReader(stream);
            if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF file");
            br.ReadInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAVE file");

            int format = 0, bits = 0;
            bool gotFmt = false;
            while (stream.Position + 8 <= stream.Length)
            {
                string id = new string(br.ReadChars(4));
                long size = br.ReadUInt32();
                long next = stream.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    format = br.ReadUInt16();
                    Channels = br.ReadUInt16();
                    SampleRate = br.ReadInt32();
                    br.ReadInt32();
                    blockAlign = br.ReadUInt16();
                    bits = br.ReadUInt16();
                    if (format == 0xFFFE && size >= 40)
                    {
                        br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32();
                        format = br.ReadUInt16(); // first 2 bytes of the sub-format GUID
                    }
                    gotFmt = true;
                }
                else if (id == "data")
                {
                    dataStart = stream.Position;
                    dataLength = Math.Min(size, stream.Length - dataStart);
                    break;
                }
                stream.Position = next;
            }

            if (!gotFmt || dataStart == 0) throw new InvalidDataException("WAV file is missing fmt/data chunks");
            isFloat = format == 3;
            if (format != 1 && format != 3) throw new NotSupportedException("Only PCM and float WAV files are supported");
            bytesPerSample = bits / 8;
            if (Channels <= 0 || bytesPerSample <= 0 || blockAlign != bytesPerSample * Channels)
                throw new InvalidDataException("Unsupported WAV layout");
            LengthSeconds = (double)dataLength / blockAlign / SampleRate;
            stream.Position = dataStart;
        }

        public double PositionSeconds
        {
            get => (double)(stream.Position - dataStart) / blockAlign / SampleRate;
            set
            {
                long frame = (long)(Math.Max(0, value) * SampleRate);
                long pos = Math.Min(frame * blockAlign, dataLength);
                stream.Position = dataStart + pos - pos % blockAlign;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            count -= count % Channels;
            long remaining = dataStart + dataLength - stream.Position;
            int bytesWanted = (int)Math.Min((long)count * bytesPerSample, remaining);
            bytesWanted -= bytesWanted % blockAlign;
            if (bytesWanted <= 0) return 0;
            if (raw.Length < bytesWanted) raw = new byte[bytesWanted];
            int got = 0;
            while (got < bytesWanted)
            {
                int n = stream.Read(raw, got, bytesWanted - got);
                if (n <= 0) break;
                got += n;
            }
            got -= got % blockAlign;
            int samples = got / bytesPerSample;
            for (int i = 0, b = 0; i < samples; i++, b += bytesPerSample)
            {
                float v;
                switch (bytesPerSample)
                {
                    case 1: v = (raw[b] - 128) / 128f; break;
                    case 2: v = (short)(raw[b] | raw[b + 1] << 8) / 32768f; break;
                    case 3: v = ((raw[b] << 8 | raw[b + 1] << 16 | raw[b + 2] << 24) >> 8) / 8388608f; break;
                    case 4: v = isFloat ? BitConverter.ToSingle(raw, b) : BitConverter.ToInt32(raw, b) / 2147483648f; break;
                    case 8: v = (float)BitConverter.ToDouble(raw, b); break;
                    default: v = 0; break;
                }
                buffer[offset + i] = v;
            }
            return samples;
        }

        public void Dispose() => stream.Dispose();
    }

    /// <summary>
    /// Wraps a file: applies a start offset, gapless looping to a loop point, and gain.
    /// Always outputs stereo at the file's native rate.
    /// </summary>
    internal sealed class TrackReader : ISampleSource
    {
        private readonly IFileSource file;
        private readonly int srcChannels;
        private readonly float gain;
        private float[] scratch = new float[0];

        public bool Loop;
        public double LoopStartSeconds;
        public bool Ended { get; private set; }
        public int LoopCount { get; private set; }

        public TrackReader(IFileSource file, double startSeconds, double loopStartSeconds, float gainDb, bool loop)
        {
            this.file = file;
            srcChannels = file.Channels;
            gain = (float)Math.Pow(10, gainDb / 20.0);
            Loop = loop;
            LoopStartSeconds = loopStartSeconds;
            if (startSeconds > 0.01) file.PositionSeconds = startSeconds;
        }

        public int SampleRate => file.SampleRate;
        public int Channels => 2;
        public double LengthSeconds => file.LengthSeconds;

        public double RemainingSeconds
        {
            get
            {
                double len = file.LengthSeconds;
                return len > 0 ? Math.Max(0, len - file.PositionSeconds) : double.MaxValue;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            int written = 0;
            int emptyReads = 0;
            while (written < frames && !Ended)
            {
                int want = (frames - written) * srcChannels;
                if (scratch.Length < want) scratch = new float[want];
                int got = file.Read(scratch, 0, want);
                int gotFrames = got / srcChannels;
                if (gotFrames <= 0)
                {
                    if (Loop && emptyReads++ < 2)
                    {
                        file.PositionSeconds = LoopStartSeconds;
                        LoopCount++;
                        continue;
                    }
                    Ended = true;
                    break;
                }
                emptyReads = 0;

                int o = offset + written * 2;
                if (srcChannels == 1)
                {
                    for (int i = 0; i < gotFrames; i++)
                    {
                        float s = scratch[i] * gain;
                        buffer[o++] = s;
                        buffer[o++] = s;
                    }
                }
                else
                {
                    for (int i = 0, si = 0; i < gotFrames; i++, si += srcChannels)
                    {
                        buffer[o++] = scratch[si] * gain;
                        buffer[o++] = scratch[si + 1] * gain;
                    }
                }
                written += gotFrames;
            }
            return written * 2;
        }

        public void Dispose() => file.Dispose();
    }
}
