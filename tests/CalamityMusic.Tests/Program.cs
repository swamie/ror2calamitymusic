using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CalamityMusic.Audio;
using CalamityMusic.Library;

namespace CalamityMusic
{
    // The mod's Log lives in Plugin.cs (BepInEx); the harness gets a console version.
    internal static class Log
    {
        public static bool Quiet;
        public static void Info(string m) { if (!Quiet) Console.WriteLine("    [info] " + m); }
        public static void Warning(string m) => Console.WriteLine("    [warn] " + m);
        public static void Error(string m) => Console.WriteLine("    [error] " + m);
    }
}

namespace CalamityMusic.Tests
{
    internal static class Program
    {
        private static int failures, checks;
        private static string fixtures, outDir;
        private const int SR = MusicEngine.SampleRate;

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "mp3debug")
            {
                foreach (string f in args.Skip(1))
                    using (var m = AudioDecoder.Open(f))
                        Console.WriteLine($"{Path.GetFileName(f)}: {m.SampleRate} Hz x{m.Channels}, len {m.LengthSeconds:0.000}s, decoded {ReadAll(m).Length / m.Channels / (double)m.SampleRate:0.000}s");
                return 0;
            }
            if (args.Length > 1 && args[0] == "preview")
            {
                // Renders the synth buildup + drop impact (no song) so you can hear it.
                Directory.CreateDirectory(args[1]);
                string silent = Path.Combine(args[1], "silence.wav");
                WriteWav(silent, new List<float>(new float[SR * 2 * 6]));
                var engine = new MusicEngine { Volume = 0.8f, BassBoostDb = 7f, BassFrequency = 90f };
                var rec = new Recorder(engine);
                engine.StartBuildup(new BuildupSynth(5f, 7));
                rec.Render(5.0);
                engine.Play(TrackInfo.FromFile(silent, "Calamity", "Generic"), false, Transition.Drop(0.8f));
                rec.WaitForLoad();
                rec.Render(3.5);
                WriteWav(Path.Combine(args[1], "buildup_preview.wav"), rec.Samples);
                Console.WriteLine("wrote buildup_preview.wav");
                return 0;
            }
            fixtures = args.Length > 0 ? args[0] : "fixtures";
            outDir = args.Length > 1 ? args[1] : "out";
            Directory.CreateDirectory(outDir);

            Run("Decoders", Decoders);
            Run("Resampler quality", Resampler);
            Run("Seamless looping", Looping);
            Run("Track names and tags", TrackNames);
            Run("Limiter", LimiterTest);
            Run("Buildup -> drop", BuildupDrop);
            Run("Bass boost", BassBoost);
            Run("Crossfade and stop", CrossfadeStop);
            Run("Song end events", SongEnd);
            Run("Tape stop, heartbeat, impact", TapeAndHeart);
            Run("Broken files don't break the mixer", BrokenFiles);
            Run("Custom buildup files", CustomBuildup);
            Run("Multi-part songs", MultiPart);
            Run("Library folders and picking", LibraryTest);
            Run("Favourites, bans, parts, tiers", LibraryExtras);
            Run("Boss phases", PhasesTest);
            Run("Boss theme mapping", ThemesTest);
            Run("Stage music", StageMusicTest);
            Run("Now Playing styles", BannerStyleTest);

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? $"ALL {checks} CHECKS PASSED" : $"{failures} of {checks} CHECKS FAILED");
            return failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            Console.WriteLine("== " + name);
            try { test(); }
            catch (Exception e) { Check(false, "threw " + e); }
        }

        private static void Check(bool ok, string what)
        {
            checks++;
            if (!ok) failures++;
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        }

        private static string F(string name) => Path.Combine(fixtures, name);
        private static string OggPath => Directory.GetFiles(fixtures, "*.ogg")[0];
        private static string Mp3Path => Directory.GetFiles(fixtures, "*.mp3")[0];

        private static float[] ReadAll(ISampleSource s, int maxFloats = int.MaxValue)
        {
            var list = new List<float>();
            var buf = new float[8192];
            int n;
            while (list.Count < maxFloats && (n = s.Read(buf, 0, buf.Length)) > 0)
                list.AddRange(buf.Take(n));
            return list.ToArray();
        }

        // ------------------------------------------------------------------ tests

        private static void Decoders()
        {
            using (var w = AudioDecoder.Open(F("base.wav")))
            {
                Check(w is WavSource && w.SampleRate == 44100 && w.Channels == 2, "wav header");
                Check(Math.Abs(w.LengthSeconds - 8.0) < 0.01, $"wav length {w.LengthSeconds:0.000}s");
                int frames = ReadAll(w).Length / 2;
                Check(Math.Abs(frames - 352800) <= 1, $"wav frames {frames}");
            }
            using (var o = AudioDecoder.Open(OggPath))
            {
                Check(o is OggSource && o.SampleRate == 44100 && o.Channels == 2, "ogg header");
                Check(Math.Abs(o.LengthSeconds - 8.0) < 0.05, $"ogg length {o.LengthSeconds:0.000}s");
                int frames = ReadAll(o).Length / 2;
                Check(Math.Abs(frames - 352800) < 3000, $"ogg frames {frames}");
            }
            using (var m = AudioDecoder.Open(Mp3Path))
            {
                Check(m is Mp3Source && m.SampleRate == 22050 && m.Channels == 1, "mp3 header (mono 22.05k)");
                Check(Math.Abs(m.LengthSeconds - 8.0) < 0.15, $"mp3 length {m.LengthSeconds:0.000}s");
                var all = ReadAll(m);
                Check(all.Length > 22050 * 7.8 && all.Max(Math.Abs) > 0.3f, $"mp3 decodes ({all.Length} samples)");
            }
            using (var h = AudioDecoder.Open(F("hires_96k.wav")))
            {
                var all = ReadAll(h);
                float peak = all.Max(Math.Abs);
                Check(h.SampleRate == 96000 && Math.Abs(peak - 0.8f) < 0.02f, $"24-bit 96k wav peak {peak:0.000}");
            }
            // A file with the wrong extension still opens by sniffing its header.
            string fake = Path.Combine(outDir, "really_an_mp3.ogg");
            File.Copy(Mp3Path, fake, true);
            using (var s = AudioDecoder.Open(fake)) Check(s is Mp3Source, "format sniffing ignores wrong extension");
        }

        private static void Resampler()
        {
            using (var r = new SincResampler(new TrackReader(AudioDecoder.Open(F("sine_44k.wav")), 0, 0, 0, false), SR))
            {
                var x = Mono(ReadAll(r, SR * 2 * 2)).Skip(4800).Take(65536).ToArray();
                var spec = Spectrum(x);
                double binHz = (double)SR / x.Length;
                int peak = Array.IndexOf(spec, spec.Skip(10).Max());
                double peakHz = peak * binHz;
                double noise = spec.Where((v, i) => i > 5 && Math.Abs(i - peak) > 12).Sum();
                double snr = 10 * Math.Log10(spec[peak] / Math.Max(noise, 1e-30));
                Check(Math.Abs(peakHz - 1000) < 2, $"44.1k->48k keeps 1 kHz at {peakHz:0.0} Hz");
                Check(snr > 85, $"44.1k->48k SNR {snr:0.0} dB");
            }
            using (var r = new SincResampler(new TrackReader(AudioDecoder.Open(F("hires_96k.wav")), 0, 0, 0, false), SR))
            {
                var x = Mono(ReadAll(r)).Skip(4800).Take(65536).ToArray();
                var spec = Spectrum(x);
                double binHz = (double)SR / x.Length;
                double at1k = Band(spec, binHz, 990, 1010);
                double at18k = Band(spec, binHz, 17900, 18100); // where a 30 kHz tone would alias
                double rejection = 10 * Math.Log10(at1k / Math.Max(at18k, 1e-30));
                Check(rejection > 80, $"96k->48k alias rejection {rejection:0.0} dB");
            }
        }

        private static void Looping()
        {
            using (var reader = new TrackReader(AudioDecoder.Open(F("base.wav")), 0, 0.5, 0, true))
            {
                var data = ReadAll(reader, 44100 * 2 * 20); // 20 s from an 8 s file
                Check(reader.LoopCount >= 2, $"looped {reader.LoopCount} times");
                Check(!reader.Ended, "looping track never ends");
                int run = 0, worst = 0;
                for (int i = 0; i < data.Length; i += 2)
                {
                    run = data[i] == 0f ? run + 1 : 0;
                    worst = Math.Max(worst, run);
                }
                Check(worst < 4, $"no gap at the loop seam (longest zero run {worst})");
            }
        }

        private static void TrackNames()
        {
            var ogg = TrackInfo.FromFile(OggPath, "Calamity", "SupremeCalamitas");
            Check(ogg.Display == "Test Artist - Ogg Song", $"filename display '{ogg.Display}'");
            Check(Math.Abs(ogg.StartSeconds - 1.0) < 1e-6 && Math.Abs(ogg.GainDb + 2) < 1e-6, "start/gain tags parsed and hidden");

            var mp3 = TrackInfo.FromFile(Mp3Path, "Calamity", "Generic");
            Check(mp3.Display == "DM DOKURO - Mp3 Tag Title", $"ID3 fallback display '{mp3.Display}'");

            var loop = TrackInfo.FromFile(Path.Combine(outDir, "Big Song [loop=1:10].wav"), "Calamity", "Yharon");
            Check(loop.Title == "Big Song" && Math.Abs(loop.LoopStartSeconds - 70) < 1e-6, "loop tag parsed");

            var junk = TrackInfo.FromFile(Path.Combine(outDir, "DM DOKURO - Universal Collapse (Official Audio) [HQ].mp3"), "Calamity", "DevourerOfGods");
            Check(junk.Display == "DM DOKURO - Universal Collapse", $"YouTube junk stripped '{junk.Display}'");
        }

        private static void LimiterTest()
        {
            var lim = new Limiter(SR);
            float peak = 0, tailPeak = 0;
            for (int i = 0; i < SR; i++)
            {
                float l = 4f * (float)Math.Sin(2 * Math.PI * 60 * i / SR), r = l;
                lim.Process(ref l, ref r);
                peak = Math.Max(peak, Math.Abs(l));
                if (i > SR / 2) tailPeak = Math.Max(tailPeak, Math.Abs(l));
            }
            Check(peak <= lim.Ceiling + 1e-6f, $"never exceeds ceiling (peak {peak:0.0000})");
            Check(tailPeak > lim.Ceiling * 0.9f, $"stays loud ({tailPeak:0.000})");
        }

        private sealed class Recorder
        {
            public readonly List<float> Samples = new List<float>();
            public readonly List<(double time, EngineEvent e)> Events = new List<(double, EngineEvent)>();
            private readonly MusicEngine engine;
            private readonly float[] block = new float[2400 * 2];
            public double Time => Samples.Count / 2.0 / SR;

            public Recorder(MusicEngine engine) { this.engine = engine; }

            public void Render(double seconds)
            {
                int blocks = (int)Math.Round(seconds * SR / 2400);
                for (int b = 0; b < blocks; b++)
                {
                    engine.RenderOffline(block, 2400);
                    Samples.AddRange(block);
                    while (engine.Events.TryDequeue(out var e)) Events.Add((Time, e));
                }
            }

            /// <summary>Background song loading happens on the thread pool; give it real time to finish.</summary>
            public void WaitForLoad() => Thread.Sleep(400);

            public double Rms(double from, double to)
            {
                int a = (int)(from * SR) * 2, b = Math.Min(Samples.Count, (int)(to * SR) * 2);
                double sum = 0;
                for (int i = a; i < b; i++) sum += Samples[i] * Samples[i];
                return Math.Sqrt(sum / Math.Max(1, b - a));
            }

            public float Peak => Samples.Max(Math.Abs);

            /// <summary>RMS above 200 Hz: a rough stand-in for perceived loudness that ignores sub rumble.</summary>
            public double Presence(double from, double to)
            {
                var hp = new Biquad();
                hp.SetHighPass(SR, 200f, 0.707f);
                int a = (int)(from * SR), b = Math.Min(Samples.Count / 2, (int)(to * SR));
                double sum = 0;
                for (int i = Math.Max(0, a - 2400); i < b; i++)
                {
                    float y = hp.Process((Samples[2 * i] + Samples[2 * i + 1]) * 0.5f);
                    if (i >= a) sum += y * y;
                }
                return Math.Sqrt(sum / Math.Max(1, b - a));
            }
        }

        private static void BuildupDrop()
        {
            var engine = new MusicEngine { Volume = 1f, BassBoostDb = 8f };
            var rec = new Recorder(engine);
            var track = TrackInfo.FromFile(OggPath, "Calamity", "SupremeCalamitas");

            engine.StartBuildup(new BuildupSynth(4f, 1234));
            rec.Render(4.0);
            engine.Play(track, true, Transition.Drop(0.8f));
            rec.WaitForLoad();
            rec.Render(8.0);
            WriteWav(Path.Combine(outDir, "buildup_drop.wav"), rec.Samples);

            Check(rec.Samples.All(s => !float.IsNaN(s) && !float.IsInfinity(s)), "no NaN/Inf");
            Check(rec.Peak <= 0.9701f, $"peak {rec.Peak:0.000} stays under the limiter ceiling");
            var started = rec.Events.Where(e => e.e.Kind == EngineEventKind.TrackStarted && e.e.Message != "loop").ToList();
            Check(started.Count == 1, $"one TrackStarted event ({started.Count})");
            Check(rec.Events.Any(e => e.e.Kind == EngineEventKind.TrackStarted && e.e.Message == "loop"), "loop is reported (song is 7 s long here)");
            if (started.Count > 0)
                Check(started[0].time > 4.0 && started[0].time < 4.2, $"song starts right after the build ({started[0].time:0.000}s)");
            double early = rec.Presence(0.2, 1.0), late = rec.Presence(3.2, 3.95);
            Check(late > early * 4, $"buildup rises (presence {early:0.000} -> {late:0.000})");
            Check(rec.Rms(3.2, 3.95) > 0.2, $"buildup ends loud enough to meet the drop (rms {rec.Rms(3.2, 3.95):0.000})");
            late = rec.Rms(3.2, 3.95);
            double gap = rec.Rms(4.06, 4.085), after = rec.Rms(4.2, 6.0);
            Check(gap < late * 0.25, $"breath of silence before the drop (rms {gap:0.0000})");
            Check(after > 0.08, $"song + impact after the drop (rms {after:0.000})");
        }

        private static void BassBoost()
        {
            double Ratio(float db)
            {
                var engine = new MusicEngine { Volume = 0.5f, BassBoostDb = db, BassFrequency = 90f };
                var rec = new Recorder(engine);
                engine.Play(TrackInfo.FromFile(F("base.wav"), "Calamity", "Generic"), true, Transition.FadeIn(0.01f));
                rec.WaitForLoad();
                rec.Render(4.0);
                var x = Mono(rec.Samples.ToArray()).Skip(SR * 2).Take(65536).ToArray();
                var spec = Spectrum(x);
                double binHz = (double)SR / x.Length;
                return 10 * Math.Log10(Band(spec, binHz, 30, 120) / Band(spec, binHz, 600, 2000));
            }
            double flat = Ratio(0f), boosted = Ratio(10f);
            Check(boosted - flat > 6, $"low end raised by {boosted - flat:0.0} dB relative to mids");
        }

        private static void CrossfadeStop()
        {
            var engine = new MusicEngine { Volume = 1f, BassBoostDb = 0f };
            var rec = new Recorder(engine);
            engine.Play(TrackInfo.FromFile(F("base.wav"), "Calamity", "A"), true, Transition.FadeIn(0.05f));
            rec.WaitForLoad();
            rec.Render(2.0);
            int firstVoice = engine.ActiveMusicVoice;
            engine.Play(TrackInfo.FromFile(OggPath, "Calamity", "B"), true, Transition.Crossfade(1f));
            rec.WaitForLoad();
            rec.Render(1.5);
            Check(engine.ActiveMusicVoice != 0 && engine.ActiveMusicVoice != firstVoice, "crossfade hands over to the new song");
            Check(rec.Rms(2.2, 3.0) > 0.05, "no dropout during crossfade");
            engine.StopAll(0.5f);
            rec.Render(1.0);
            Check(rec.Rms(4.05, 4.45) < 1e-4, "silent after StopAll");
            Check(!engine.AnythingPlaying, "all voices released");
        }

        private static void SongEnd()
        {
            var engine = new MusicEngine { Volume = 1f };
            var rec = new Recorder(engine);
            engine.Play(TrackInfo.FromFile(F("base.wav"), "Calamity", "A"), false, Transition.FadeIn(0.01f));
            rec.WaitForLoad();
            rec.Render(9.0);
            var ending = rec.Events.FirstOrDefault(e => e.e.Kind == EngineEventKind.TrackEnding);
            var ended = rec.Events.FirstOrDefault(e => e.e.Kind == EngineEventKind.TrackEnded);
            Check(ending.e.Kind == EngineEventKind.TrackEnding && ending.time > 4.5 && ending.time < 5.5, $"TrackEnding ~3 s early ({ending.time:0.00}s)");
            Check(ended.e.Kind == EngineEventKind.TrackEnded && ended.time > 7.9, $"TrackEnded at the end ({ended.time:0.00}s)");
        }

        private static void TapeAndHeart()
        {
            // Tape stop: the song slows to a halt, stays paused, then spins back up where it left off.
            var engine = new MusicEngine { Volume = 1f, BassBoostDb = 0f };
            var rec = new Recorder(engine);
            engine.Play(TrackInfo.FromFile(F("base.wav"), "Calamity", "A"), false, Transition.FadeIn(0.01f));
            rec.WaitForLoad();
            rec.Render(2.0);
            engine.TapeStop(1.0f);
            rec.Render(3.0); // 1 s slowing down + 2 s stopped
            double before = rec.Rms(1.5, 2.0), during = rec.Rms(2.1, 2.6), stopped = rec.Rms(3.5, 5.0);
            Check(during > 0.02 && stopped < 1e-4, $"tape slows then goes silent (rms {before:0.000} -> {during:0.000} -> {stopped:0.00000})");
            engine.TapeStart(0.5f);
            rec.Render(1.5);
            Check(rec.Rms(5.6, 6.5) > before * 0.5, $"tape spins back up (rms {rec.Rms(5.6, 6.5):0.000})");
            rec.Render(8.0);
            var ended = rec.Events.FirstOrDefault(e => e.e.Kind == EngineEventKind.TrackEnded);
            // 8 s file, ~2.5 s of playback "lost" to the stop: it should end around 10.5 s, not 8 s.
            Check(ended.time > 9.8 && ended.time < 11.5, $"song paused during the stop and resumed in place (ended at {ended.time:0.00}s)");

            // StopAll while stopped clears everything and resets the speed.
            var e2 = new MusicEngine { Volume = 1f };
            var r2 = new Recorder(e2);
            e2.Play(TrackInfo.FromFile(F("base.wav"), "Calamity", "A"), true, Transition.FadeIn(0.01f));
            r2.WaitForLoad();
            r2.Render(1.5);
            e2.TapeStop(0.3f);
            r2.Render(1.0);
            double stopAt = r2.Time;
            e2.StopAll(1f);
            r2.Render(0.3);
            Check(!e2.AnythingPlaying, "stopping during a tape stop releases everything");
            Check(r2.Rms(stopAt, stopAt + 0.3) < 1e-4, $"no burst of the song when it's stopped mid tape stop (rms {r2.Rms(stopAt, stopAt + 0.3):0.00000})");
            e2.Play(TrackInfo.FromFile(F("base.wav"), "Calamity", "A"), true, Transition.FadeIn(0.01f));
            r2.WaitForLoad();
            r2.Render(1.5);
            Check(r2.Rms(r2.Time - 1.0, r2.Time) > 0.05, "next song plays at normal speed");

            // Heartbeat at low health (needs something playing; use near-silence).
            string quiet = Path.Combine(outDir, "quiet.wav");
            WriteWav(quiet, new List<float>(new float[SR * 2 * 6]));
            double Heart(float low)
            {
                var e = new MusicEngine { Volume = 1f, BassBoostDb = 0f, LowHealth = low };
                var r = new Recorder(e);
                e.Play(TrackInfo.FromFile(quiet, "Calamity", "A"), true, Transition.FadeIn(0.01f));
                r.WaitForLoad();
                r.Render(3.0);
                return r.Rms(1.5, 3.0);
            }
            double none = Heart(0f), heart = Heart(1f);
            Check(none < 1e-5 && heart > 0.03, $"heartbeat only at low health (rms {none:0.00000} vs {heart:0.000})");

            // The beat stays steady when health moves after a long time at low health.
            {
                var e = new MusicEngine { Volume = 1f, BassBoostDb = 0f, LowHealth = 0.5f };
                var r = new Recorder(e);
                e.Play(TrackInfo.FromFile(quiet, "Calamity", "A"), true, Transition.FadeIn(0.01f));
                r.WaitForLoad();
                r.Render(20.0);
                e.LowHealth = 0.56f;
                double from = r.Time;
                r.Render(4.0);
                var env = new List<double>();
                for (double t = from; t < from + 4.0; t += 0.01) env.Add(r.Rms(t, t + 0.01));
                double peak = env.Max(), thr = peak * 0.25;
                var onsets = new List<double>();
                for (int i = 1; i < env.Count; i++) if (env[i] > thr && env[i - 1] <= thr) onsets.Add(i * 0.01);
                var gaps = onsets.Zip(onsets.Skip(1), (a, b) => b - a).ToList();
                // Lub to dub is 0.24 s; dub to the next lub is the rest of a ~0.63 s beat.
                bool steady = gaps.Count >= 8 && gaps.All(g => Math.Abs(g - 0.24) < 0.05 || (g > 0.33 && g < 0.45));
                Check(steady, $"heartbeat stays on time when health changes ({string.Join(" ", gaps.Select(g => g.ToString("0.00")))})");
            }

            var e3 = new MusicEngine { Volume = 1f };
            var r3 = new Recorder(e3);
            e3.Play(TrackInfo.FromFile(quiet, "Calamity", "A"), true, Transition.FadeIn(0.01f));
            r3.WaitForLoad();
            r3.Render(0.5);
            e3.PlayImpact(0.8f);
            r3.Render(1.0);
            Check(r3.Rms(0.55, 1.2) > 0.05, "standalone impact hit plays");
        }

        private static void BrokenFiles()
        {
            // Half of a real OGG followed by junk: decodes fine for a while, then fails mid-stream.
            byte[] good = File.ReadAllBytes(OggPath);
            byte[] bad = new byte[good.Length];
            Array.Copy(good, bad, good.Length / 3);
            new Random(5).NextBytes(new Span<byte>(bad, good.Length / 3, bad.Length - good.Length / 3));
            for (int i = good.Length / 3; i + 4 < bad.Length; i += 4096) { bad[i] = (byte)'O'; bad[i + 1] = (byte)'g'; bad[i + 2] = (byte)'g'; bad[i + 3] = (byte)'S'; }
            string corrupt = Path.Combine(outDir, "Broken - Corrupt Song.ogg");
            File.WriteAllBytes(corrupt, bad);
            string garbage = Path.Combine(outDir, "Broken - Garbage.mp3");
            var junk = new byte[50000];
            new Random(9).NextBytes(junk);
            File.WriteAllBytes(garbage, junk);

            var engine = new MusicEngine { Volume = 1f };
            var rec = new Recorder(engine);
            engine.StartBuildup(new BuildupSynth(3f, 1));
            rec.Render(0.5);
            engine.Play(TrackInfo.FromFile(garbage, "Calamity", "X"), true, Transition.Crossfade(0.2f));
            rec.WaitForLoad();
            rec.Render(0.5);
            Check(rec.Rms(0.6, 1.0) > 0.001, "buildup keeps playing when a garbage file fails to load");
            Check(rec.Events.Any(e => e.e.Kind == EngineEventKind.LoadFailed), "garbage file is rejected with LoadFailed");

            engine.Play(TrackInfo.FromFile(corrupt, "Calamity", "X"), false, Transition.Crossfade(0.2f));
            rec.WaitForLoad();
            rec.Render(6.0);
            Check(rec.Samples.All(f => !float.IsNaN(f)), "no NaN from a corrupt stream");
            Check(rec.Events.Any(e => e.e.Kind == EngineEventKind.TrackEnded || e.e.Kind == EngineEventKind.LoadFailed),
                "corrupt stream ends or reports failure: " + string.Join(", ", rec.Events.Select(e => e.e.Kind + (e.e.Message != null ? "(" + e.e.Message + ")" : ""))));

            // The mixer must still work afterwards.
            engine.Play(TrackInfo.FromFile(F("base.wav"), "Calamity", "A"), true, Transition.FadeIn(0.05f));
            rec.WaitForLoad();
            rec.Render(2.0);
            Check(rec.Rms(rec.Time - 1.0, rec.Time) > 0.05, "mixer still plays the next song");
        }

        private static void CustomBuildup()
        {
            var engine = new MusicEngine { Volume = 1f };
            var rec = new Recorder(engine);
            engine.StartBuildupFile(F("base.wav"));
            rec.WaitForLoad();
            rec.Render(2.0);
            var ready = rec.Events.FirstOrDefault(e => e.e.Kind == EngineEventKind.BuildupReady);
            Check(ready.e.Kind == EngineEventKind.BuildupReady && Math.Abs(ready.e.Seconds - 8.0) < 0.01, $"BuildupReady reports the file length ({ready.e.Seconds:0.00}s)");
            Check(rec.Rms(1.2, 2.0) > 0.05, "custom buildup plays");

            engine.StartBuildupFile(Path.Combine(outDir, "does_not_exist.ogg"));
            rec.WaitForLoad();
            rec.Render(0.2);
            Check(rec.Events.Any(e => e.e.Kind == EngineEventKind.BuildupFailed), "missing buildup file reports BuildupFailed");

            var held = new BuildupSynth(4f, 3, true);
            var buf = new float[SR];
            held.Read(buf, 0, buf.Length);
            double rms = Math.Sqrt(buf.Skip(SR / 2).Select(v => (double)v * v).Average());
            Check(rms > 0.05, $"hold-only synth starts at full tension (rms {rms:0.000})");
        }

        private static void MultiPart()
        {
            string root = Path.Combine(outDir, "seqlib", "Music");
            if (Directory.Exists(Path.Combine(outDir, "seqlib"))) Directory.Delete(Path.Combine(outDir, "seqlib"), true);
            string dir = Path.Combine(root, "Calamity", "SupremeCalamitas");
            Directory.CreateDirectory(dir);
            // Three 2-second parts (cut from the fixture) so the hand-over happens quickly.
            for (int i = 1; i <= 3; i++)
                WriteWav(Path.Combine(dir, $"DM DOKURO - Stained, Brutal Calamity [seq=scal {i}].wav"), Cut(F("base.wav"), 1.0 + i, 2.0));
            File.Copy(F("base.wav"), Path.Combine(dir, "DM DOKURO - Stained, Brutal Calamity.wav"), true);
            var lib = new MusicLibrary(root);
            lib.Rescan();

            var part1 = TrackInfo.FromFile(Path.Combine(dir, "DM DOKURO - Stained, Brutal Calamity [seq=scal 1].wav"), "Calamity", "SupremeCalamitas");
            Check(part1.Sequence == "scal" && part1.SequenceIndex == 1 && part1.Display == "DM DOKURO - Stained, Brutal Calamity", "seq tag parsed and hidden");
            var picks = new HashSet<string>();
            for (int i = 0; i < 30; i++) picks.Add(Path.GetFileName(lib.Pick(new[] { "SupremeCalamitas" }, MusicMode.Calamity, null, true, out _).Path));
            Check(picks.Count == 2 && !picks.Any(p => p.Contains("scal 2") || p.Contains("scal 3")), "fights only start at part 1: " + string.Join(", ", picks));
            var part2 = lib.NextInSequence(part1);
            Check(part2 != null && part2.SequenceIndex == 2 && lib.NextInSequence(part2)?.SequenceIndex == 3, "next part found in order");
            Check(lib.IsContinuation(part2) && !lib.IsContinuation(part1), "continuation detection");

            // Gapless hand-over in the engine.
            var engine = new MusicEngine { Volume = 1f, BassBoostDb = 0f };
            var rec = new Recorder(engine);
            engine.Play(part1, false, Transition.FadeIn(0.01f));
            rec.WaitForLoad();
            rec.Render(0.5);
            engine.Play(part2, true, Transition.Append());
            rec.WaitForLoad();
            rec.Render(3.0);
            var starts = rec.Events.Where(e => e.e.Kind == EngineEventKind.TrackStarted).ToList();
            Check(starts.Count == 2 && Math.Abs(starts[1].time - 2.0) < 0.06, $"part 2 starts right as part 1 ends ({(starts.Count > 1 ? starts[1].time : 0):0.000}s)");
            int run = 0, worst = 0;
            for (int i = (int)(1.9 * SR) * 2; i < (int)(2.1 * SR) * 2; i += 2) { run = rec.Samples[i] == 0f ? run + 1 : 0; worst = Math.Max(worst, run); }
            Check(worst < 4, $"no silence at the seam (longest zero run {worst})");
            Check(engine.ActiveMusicVoice != 0, "part 2 is now the active song");
        }

        private static List<float> Cut(string wav, double from, double seconds)
        {
            using (var s = AudioDecoder.Open(wav))
            {
                var reader = new SincResampler(new TrackReader(s, from, 0, 0, false), SR);
                var buf = new float[(int)(seconds * SR) * 2];
                int got = 0, n;
                while (got < buf.Length && (n = reader.Read(buf, got, buf.Length - got)) > 0) got += n;
                return buf.Take(got).ToList();
            }
        }

        private static void LibraryTest()
        {
            string root = Path.Combine(outDir, "lib", "Music");
            if (Directory.Exists(Path.Combine(outDir, "lib"))) Directory.Delete(Path.Combine(outDir, "lib"), true);
            var lib = new MusicLibrary(root);
            lib.EnsureFolders();
            Check(Directory.Exists(Path.Combine(root, "Calamity", "SupremeCalamitas")), "creates Calamity boss folders");
            Check(Directory.Exists(Path.Combine(root, "Infernum", "SupremeCalamitas")) && !Directory.Exists(Path.Combine(root, "Infernum", "Crabulon")), "creates Infernum folders only where Infernum has a track");
            Check(File.Exists(Path.Combine(root, "HOW_TO_ADD_SONGS.txt")), "writes song guide");

            void Put(string rel, string src)
            {
                string p = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.Copy(src, p, true);
            }
            Put("Calamity/SupremeCalamitas/DM DOKURO - Stained, Brutal Calamity.ogg", OggPath);
            Put("Calamity/SupremeCalamitas/DM DOKURO - SCal Alt.ogg", OggPath);
            Put("Infernum/SupremeCalamitas/PinpinNeon - Scars of Calamity.ogg", OggPath);
            Put("Calamity/Generic/Any - Generic Song.mp3", Mp3Path);
            Put("Extensions/NamelessDeity/ENNWAY! - Edenic Whispers.wav", F("base.wav"));
            Put("MyPack/Yharon/Me - Custom Yharon.wav", F("base.wav"));
            Put("Calamity/MoonEscape/DM DOKURO - UNIVERSAL COLLAPSE.wav", F("base.wav"));
            Put("Buildups/Riser.wav", F("base.wav"));
            lib.Rescan();
            Check(lib.TrackCount == 7 && lib.BuildupCount == 1, $"indexed {lib.TrackCount} songs, {lib.BuildupCount} buildup");

            string P(MusicMode mode, params string[] slots) => lib.Pick(slots, mode, null, true, out _)?.Title;
            Check(P(MusicMode.Calamity, "SupremeCalamitas").StartsWith("S"), "Calamity mode uses the Calamity folder");
            Check(P(MusicMode.Infernum, "SupremeCalamitas") == "Scars of Calamity", "Infernum mode prefers the Infernum version");
            Check(P(MusicMode.Infernum, "NamelessDeity") == "Edenic Whispers", "Infernum mode falls back to Calamity/Extensions");
            Check(P(MusicMode.Calamity, "Yharon") == "Custom Yharon", "custom pack folders count as extensions");
            var cryo = lib.Pick(new[] { "Cryogen" }, MusicMode.Calamity, null, true, out bool fb);
            Check(cryo?.Title == "Generic Song" && fb, "empty theme falls back to Generic");
            var seen = new HashSet<string>();
            for (int i = 0; i < 40; i++) seen.Add(P(MusicMode.Mixed, "SupremeCalamitas"));
            Check(seen.Count == 3, $"Mixed mode shuffles every pack ({seen.Count} distinct)");
            string a = P(MusicMode.Calamity, "SupremeCalamitas"), b = P(MusicMode.Calamity, "SupremeCalamitas"), c = P(MusicMode.Calamity, "SupremeCalamitas");
            Check(a != b && b != c, "doesn't repeat the same song twice in a row");
            Check(lib.PickEscape(MusicMode.Calamity)?.Title == "UNIVERSAL COLLAPSE", "moon escape folder");

            File.Delete(Path.Combine(root, "Calamity/Generic/Any - Generic Song.mp3"));
            lib.Rescan();
            var any = lib.Pick(new[] { "Cryogen" }, MusicMode.Calamity, null, true, out _);
            Check(any != null && any.Slot != "MoonEscape", $"any-song fallback skips the escape folder ({any?.Slot})");
            Check(lib.Pick(new[] { "Cryogen" }, MusicMode.Calamity, null, false, out _) == null, "fallback can be turned off");
        }

        private static void LibraryExtras()
        {
            string baseDir = Path.Combine(outDir, "extras");
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            string root = Path.Combine(baseDir, "Music");
            void Put(string rel)
            {
                string p2 = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p2));
                File.Copy(F("base.wav"), p2, true);
            }
            Put("Calamity/Yharon/A - One.wav");
            Put("Calamity/Yharon/A - Two.wav");
            Put("Calamity/Yharon/A - Three.wav");
            for (int i = 1; i <= 5; i++) Put($"Calamity/BossRush/CDMusic - Boss Rush Tier {i} [seq=bossrush {i}].wav");
            Put("Calamity/SupremeCalamitas/DM DOKURO - SBC [seq=scal 1].wav");
            Put("Calamity/SupremeCalamitas/DM DOKURO - SBC [seq=scal 2].wav");
            Put("Calamity/SupremeCalamitas/DM DOKURO - SBC [seq=scal 3].wav");
            var lib = new MusicLibrary(root);
            lib.Rescan();

            var one = lib.Pick(new[] { "Yharon" }, MusicMode.Calamity, null, false, out _);
            string rel = lib.RelPath(one);
            Check(rel.StartsWith("Calamity/Yharon/") && lib.FindByRelPath(rel)?.Path == one.Path, $"relative paths round-trip ({rel})");

            var two = lib.FindByRelPath("Calamity/Yharon/A - Two.wav");
            Check(lib.ToggleFavorite(two) && lib.IsFavorite(two), "favourite toggled on");
            int favHits = 0;
            // With "don't repeat the last song", a favourite lands ~43% of picks vs ~33% normally.
            for (int i = 0; i < 3000; i++) if (lib.Pick(new[] { "Yharon" }, MusicMode.Calamity, null, false, out _).Title == "Two") favHits++;
            Check(favHits > 1150, $"favourites are picked more often ({favHits}/3000, ~1000 would be normal)");

            var three = lib.FindByRelPath("Calamity/Yharon/A - Three.wav");
            lib.Ban(three);
            bool sawBanned = false;
            for (int i = 0; i < 200; i++) if (lib.Pick(new[] { "Yharon" }, MusicMode.Calamity, null, false, out _).Title == "Three") sawBanned = true;
            Check(!sawBanned && lib.BannedCount == 1, "banned song never picked");
            var reloaded = new MusicLibrary(root);
            reloaded.Rescan();
            Check(reloaded.IsFavorite(reloaded.FindByRelPath("Calamity/Yharon/A - Two.wav")) && reloaded.BannedCount == 1, "favourites and bans persist");
            reloaded.ClearBans();
            Check(reloaded.BannedCount == 0 && File.ReadAllText(Path.Combine(baseDir, "banned.txt")).Split('\n').All(l => l.StartsWith("#") || l.Trim().Length == 0), "bans can be cleared");

            var p1 = lib.FindByRelPath("Calamity/SupremeCalamitas/DM DOKURO - SBC [seq=scal 1].wav");
            Check(lib.SequenceLength(p1) == 3 && lib.PartAt(p1, 3)?.SequenceIndex == 3 && lib.PartPosition(lib.PartAt(p1, 2)) == 2, "multi-part lookups");
            Check(lib.SequenceLength(one) == 1, "normal songs have one part");
            Check(lib.PickTier("BossRush", 1, MusicMode.Calamity)?.SequenceIndex == 1 && lib.PickTier("BossRush", 3, MusicMode.Calamity)?.SequenceIndex == 3
                  && lib.PickTier("BossRush", 9, MusicMode.Calamity)?.SequenceIndex == 5, "Boss Rush tier picking");

            // Favouriting any part favourites the whole song (picks only ever see part 1).
            Check(lib.ToggleFavorite(lib.PartAt(p1, 2)) && lib.IsFavorite(p1) && lib.IsFavorite(lib.PartAt(p1, 3)), "favouriting part 2 favourites the song");
            lib.ToggleFavorite(p1);
            Check(!lib.IsFavorite(lib.PartAt(p1, 2)), "and unfavouriting works from any part");

            // Banning a multi-part song bans all of its parts, so a fight never starts on part 2.
            lib.BanSong(lib.PartAt(p1, 2));
            bool sawScal = false;
            for (int i = 0; i < 50; i++) if (lib.Pick(new[] { "SupremeCalamitas" }, MusicMode.Calamity, null, false, out _) != null) sawScal = true;
            Check(!sawScal && lib.IsBanned(p1) && lib.IsBanned(lib.FindByRelPath("Calamity/SupremeCalamitas/DM DOKURO - SBC [seq=scal 3].wav")),
                  "banning a song bans every part");
            Check(lib.PartAt(p1, 1) == null && lib.SequenceLength(p1) == 1, "banned parts are never handed out");
            lib.ClearBans();
            Check(lib.Pick(new[] { "SupremeCalamitas" }, MusicMode.Calamity, null, false, out _)?.SequenceIndex == 1, "unbanned song starts at part 1 again");
        }

        private static void PhasesTest()
        {
            Check(BossPhases.PartForPhase(1, 4, 3) == 1 && BossPhases.PartForPhase(2, 4, 3) == 2
                  && BossPhases.PartForPhase(3, 4, 3) == 2 && BossPhases.PartForPhase(4, 4, 3) == 3, "Mithrix's 4 phases over 3 parts: 1, 2, 2, 3");
            Check(BossPhases.PartForPhase(1, 3, 2) == 1 && BossPhases.PartForPhase(2, 3, 2) == 2 && BossPhases.PartForPhase(3, 3, 2) == 2, "3 phases over 2 parts");
            Check(BossPhases.PartForPhase(2, 3, 3) == 2 && BossPhases.PartForPhase(3, 3, 5) == 5, "phases spread to the last part");
            Check(BossPhases.PartForPhase(4, 4, 1) == 1 && BossPhases.PartForPhase(9, 4, 3) == 3 && BossPhases.PartForPhase(0, 4, 3) == 1, "out-of-range phases clamp");

            Check(BossPhases.FromBodyName("MiniVoidRaidCrabBodyPhase2", out int p, out int n) && p == 2 && n == 3, "Voidling phase from body");
            Check(BossPhases.FromBodyName("FalseSonBossBody", out p, out n) && p == 1 && n == 3, "False Son phase 1");
            Check(BossPhases.FromBodyName("FalseSonBossBodyLunarShard", out p, out _) && p == 2, "False Son phase 2");
            Check(BossPhases.FromBodyName("FalseSonBossBodyBrokenLunarShard", out p, out _) && p == 3, "False Son phase 3");
            Check(BossPhases.FromBodyName("BrotherHurtBody", out p, out n) && p == 4 && n == 4, "hurt Mithrix is his last phase");
            Check(!BossPhases.FromBodyName("BrotherBody", out _, out _) && !BossPhases.FromBodyName("TitanBody", out _, out _), "no phase from plain bodies");
            Check(BossPhases.FromBrotherState("Phase3") == 3 && BossPhases.FromBrotherState("PreEncounter") == 0 && BossPhases.FromBrotherState("BossDeath") == 0, "Mithrix encounter states");
            Check(BossPhases.Family("BrotherHurtBody") == "BrotherBody" && BossPhases.Family("MiniVoidRaidCrabBodyPhase3") == "MiniVoidRaidCrabBody"
                  && BossPhases.Family("FalseSonBossBodyLunarShard") == "FalseSonBossBody" && BossPhases.Family("TitanBody") == "TitanBody", "one title card per boss");

            Check(BossPhases.BossRushTier(0) == 1 && BossPhases.BossRushTier(1) == 1 && BossPhases.BossRushTier(10) == 1
                  && BossPhases.BossRushTier(11) == 2 && BossPhases.BossRushTier(35) == 4 && BossPhases.BossRushTier(41) == 5 && BossPhases.BossRushTier(400) == 5,
                  "Simulacrum waves to Boss Rush tiers");
        }

        private static void ThemesTest()
        {
            string dir = Path.Combine(outDir, "cfg");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var themes = new BossThemes(dir);

            var mithrix = themes.Resolve("BrotherBody", "Mithrix");
            Check(mithrix.Slots.SequenceEqual(new[] { "SupremeCalamitas" }) && mithrix.IsFinale, "Mithrix -> Supreme Calamitas (finale)");
            Check(themes.Resolve("BrotherHurtBody", null).SharesSlotWith(mithrix), "Mithrix phase 4 continues the same song");
            Check(themes.Resolve("VagrantBody", "Wandering Vagrant").Slots[0] == "LeviathanAnahita", "jellyfish -> aquatic");
            Check(themes.Resolve("LampBossBody", "Wayfarer").Slots.Contains("ProfanedGuardians"), "Starstorm 2 Wayfarer mapped");
            Check(themes.Resolve("NemCommandoBody", "Nemesis Commando").Slots[0] == "CalamitasClone", "Starstorm 2 Nemesis Commando mapped");
            Check(themes.Resolve("LemurianBruiserBody", "Elder Lemurian").Slots[0] == "Yharon", "unknown lizard guessed as Yharon");
            Check(themes.Resolve("LightningGolemBody", null).Slots[0] == "StormWeaver", "keyword order: lightning beats light");
            var heart = themes.Resolve("SolusHeartBody", "Solus Heart");
            Check(heart.Slots[0] == "ExoMechs" && heart.IsFinale, "new DLC final boss guessed as Exo Mechs finale");
            Check(themes.Resolve("ProvidenceBody", "Providence").Slots[0] == "Providence", "Providence -> Providence");
            Check(themes.Resolve("GnorpBody", "Gnorp").Slots[0] == Slots.Generic, "nameless modded boss -> Generic");
            Check(themes.Resolve("EnemyGuyBody", null).Slots[0] == Slots.Generic, "'nem' doesn't match inside 'Enemy'");
            Check(themes.Resolve("PoliceBody", null).Slots[0] == Slots.Generic, "'ice' doesn't match inside 'Police'");
            Check(themes.Resolve("MiniVoidRaidCrabBodyPhase4", null).IsFinale, "multi-word keyword (voidraid) still matches");

            string cfg = File.ReadAllText(Path.Combine(dir, "CalamityMusic.BossThemes.cfg"));
            Check(cfg.Contains("GnorpBody = Generic") && cfg.Contains("BrotherBody = SupremeCalamitas"), "config lists defaults and auto-added bosses");

            File.WriteAllText(Path.Combine(dir, "CalamityMusic.BossThemes.cfg"), cfg.Replace("VagrantBody = LeviathanAnahita, AquaticScourge", "VagrantBody = Vanilla"));
            themes.Reload();
            Check(themes.Resolve("VagrantBody", null).KeepVanilla, "editing the config to Vanilla keeps the game's music");
        }

        private static void StageMusicTest()
        {
            string dir = Path.Combine(outDir, "stagecfg");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var stages = new StageThemes(dir);
            Check(stages.Resolve("title", "Main Menu").Slots[0] == "Biome_Title", "main menu -> title theme");
            Check(stages.Resolve("lobby", null).SharesSlotWith(stages.Resolve("title", null)), "character select keeps the menu song going");
            Check(stages.Resolve("bazaar", "Bazaar Between Time").Slots[0] == "Biome_SunkenSea", "Bazaar has stage music");
            Check(stages.Resolve("voidstage", "Void Locus").Slots[0] == "Biome_Abyss4", "Void Locus -> Abyss");
            Check(stages.Resolve("sulfurpools", null).Slots[0] == "Biome_SulphurousSea", "Sulfur Pools -> Sulphurous Sea");
            Check(stages.Resolve("meridian", "Prime Meridian").Slots[0] == "Biome_EternalGarden", "Prime Meridian (DLC2) mapped");
            Check(stages.Resolve("intro", null).KeepVanilla, "opening cutscene keeps vanilla");
            Check(stages.Resolve("computationalexchange", "Computational Exchange").Slots[0] == "Biome_Lab", "unknown machine stage guessed as lab");
            Check(stages.Resolve("frostbitepeaks", null).Slots[0] == "Biome_Blizzard", "unknown snowy stage guessed as blizzard");
            Check(stages.Resolve("starfallruins", null).Slots[0] == "Biome_Astral", "'star' beats the 'tar' rule");
            Check(stages.Resolve("weirdmodstage", "Gnorp Land").Slots[0] == Slots.AnyBiome, "anything else -> any area theme");
            string cfg = File.ReadAllText(Path.Combine(dir, "CalamityMusic.StageThemes.cfg"));
            Check(cfg.Contains("bazaar = Biome_SunkenSea") && cfg.Contains("weirdmodstage = Biome_Any"), "stage config lists defaults and new stages");

            string root = Path.Combine(outDir, "stagelib", "Music");
            if (Directory.Exists(Path.Combine(outDir, "stagelib"))) Directory.Delete(Path.Combine(outDir, "stagelib"), true);
            void Put(string rel)
            {
                string p2 = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p2));
                File.Copy(F("base.wav"), p2, true);
            }
            Put("Calamity/Biome_Title/DM DOKURO - The Tale of a Cruel World.wav");
            Put("Calamity/Biome_Abyss4/DM DOKURO - void.wav");
            Put("Calamity/Biome_SunkenSea/DM DOKURO - sanctuary.wav");
            Put("Calamity/SupremeCalamitas/DM DOKURO - Stained, Brutal Calamity.wav");
            var lib = new MusicLibrary(root);
            lib.Rescan();
            Check(lib.HasStageMusic, "library knows it has stage music");
            Check(lib.PickStage(new[] { "Biome_Abyss4" }, MusicMode.Calamity, null)?.Title == "void", "stage picks its own area theme");
            var any = new HashSet<string>();
            for (int i = 0; i < 30; i++) any.Add(lib.PickStage(new[] { "Biome_Blizzard" }, MusicMode.Calamity, null)?.Title);
            Check(any.SetEquals(new[] { "void", "sanctuary" }), "missing area theme falls back to other area themes, never the menu or a boss song: " + string.Join(", ", any));
            Check(lib.Pick(new[] { "Cryogen" }, MusicMode.Calamity, null, true, out _)?.Title == "Stained, Brutal Calamity", "boss fallback never picks calm area themes");
            Check(lib.RandomFilledSlot(MusicMode.Calamity) == "SupremeCalamitas", "preview uses boss themes only");
        }

        private static void BannerStyleTest()
        {
            TrackInfo T(string slot, string pack = "Calamity") => TrackInfo.FromFile("x/DM DOKURO - Song.ogg", pack, slot);
            Check(CalamityMusic.UI.BannerStyles.For(T("SupremeCalamitas")).Name == "Brimstone", "Supreme Calamitas -> brimstone embers");
            Check(CalamityMusic.UI.BannerStyles.For(T("DevourerOfGods")).Name == "Cosmic", "Devourer of Gods -> cosmic void");
            Check(CalamityMusic.UI.BannerStyles.For(T("ExoMechs")).Glitch, "Exo Mechs glitch");
            Check(CalamityMusic.UI.BannerStyles.For(T("StormWeaver")).Lightning, "Storm Weaver lightning");
            Check(CalamityMusic.UI.BannerStyles.For(T("Biome_SunkenSea")).Name == "Ocean", "Sunken Sea -> ocean bubbles");
            Check(CalamityMusic.UI.BannerStyles.For(T("Generic")).Name == "Infernum", "Generic keeps the Infernum look");
            Check(CalamityMusic.UI.BannerStyles.For(T("MyFrostBoss")).Name == "Frost", "custom folders guessed from their name");
            Check(CalamityMusic.UI.BannerStyles.For(T(Slots.BossRush)).Name == "Boss Rush" && CalamityMusic.UI.BannerStyles.BossRush.Lightning, "Boss Rush style");
            Check(CalamityMusic.UI.BannerStyles.ForSlot("Yharon").Name == "Holy Fire", "style by slot name (title cards)");
            var unstyled = Slots.All.Where(sd => sd.Name != Slots.Generic && sd.Name != Slots.Title
                && CalamityMusic.UI.BannerStyles.For(T(sd.Name)) == CalamityMusic.UI.BannerStyles.Default).Select(sd => sd.Name).ToList();
            Check(unstyled.Count == 0, "every boss/area folder has its own style: " + string.Join(", ", unstyled));
            Check(CalamityMusic.UI.BannerStyles.ThemeName(T("SupremeCalamitas")) == "Supreme Witch, Calamitas", "theme name for a boss");
            Check(CalamityMusic.UI.BannerStyles.ThemeName(T("Biome_SulphurousSea")) == "Sulphurous Sea", "theme name for an area, note trimmed");
            Check(CalamityMusic.UI.BannerStyles.ThemeName(T("AvatarOfEmptiness", "Extensions")) == "Avatar of Emptiness", "add-on name trimmed");
            Check(CalamityMusic.UI.BannerStyles.ThemeName(T("MyCoolBoss")) == "My Cool Boss", "custom folder name made readable");
            Check(CalamityMusic.UI.BannerStyles.ThemeName(T("Generic")) == null, "no subtitle for Generic");
            foreach (var st in CalamityMusic.UI.BannerStyles.All)
                Check(new[] { st.Fill, st.Outline, st.Glow, st.Accent, st.Backdrop }.All(h => System.Text.RegularExpressions.Regex.IsMatch(h, "^#[0-9A-Fa-f]{6}$")), $"{st.Name} colours are valid hex");
        }

        // ------------------------------------------------------------------ helpers

        private static float[] Mono(float[] stereo)
        {
            var m = new float[stereo.Length / 2];
            for (int i = 0; i < m.Length; i++) m[i] = (stereo[2 * i] + stereo[2 * i + 1]) * 0.5f;
            return m;
        }

        /// <summary>Power spectrum with a Blackman-Harris window (length must be a power of two).</summary>
        private static double[] Spectrum(float[] x)
        {
            int n = x.Length;
            var re = new double[n];
            var im = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / (n - 1);
                double w = 0.35875 - 0.48829 * Math.Cos(2 * Math.PI * t) + 0.14128 * Math.Cos(4 * Math.PI * t) - 0.01168 * Math.Cos(6 * Math.PI * t);
                re[i] = x[i] * w;
            }
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                for (int i = 0; i < n; i += len)
                    for (int k = 0; k < len / 2; k++)
                    {
                        double cr = Math.Cos(ang * k), ci = Math.Sin(ang * k);
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                    }
            }
            var p = new double[n / 2];
            for (int i = 0; i < p.Length; i++) p[i] = re[i] * re[i] + im[i] * im[i];
            return p;
        }

        private static double Band(double[] spec, double binHz, double lo, double hi)
        {
            double s = 0;
            for (int i = (int)(lo / binHz); i <= (int)(hi / binHz) && i < spec.Length; i++) s += spec[i];
            return s;
        }

        private static void WriteWav(string path, List<float> stereo)
        {
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                int bytes = stereo.Count * 2;
                bw.Write("RIFF".ToCharArray()); bw.Write(36 + bytes); bw.Write("WAVE".ToCharArray());
                bw.Write("fmt ".ToCharArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)2);
                bw.Write(SR); bw.Write(SR * 4); bw.Write((short)4); bw.Write((short)16);
                bw.Write("data".ToCharArray()); bw.Write(bytes);
                foreach (float f in stereo) bw.Write((short)Math.Max(-32768, Math.Min(32767, f * 32767f)));
            }
        }
    }
}
