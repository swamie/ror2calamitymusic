using System;

namespace CalamityMusic.Audio
{
    /// <summary>
    /// Procedurally generated EDM-style riser that plays while the teleporter turns red.
    /// Eight beats fill the requested length: kicks on the first four beats, a snare roll that speeds
    /// up from quarter notes to 32nds, a filtered noise sweep and a detuned supersaw that climbs an
    /// octave. If the boss has not shown up when the build finishes it holds a tense sustained roll
    /// until the music engine cuts it for the drop. It never ends by itself.
    /// </summary>
    internal sealed class BuildupSynth : ISampleSource
    {
        private const float SR = MusicEngine.SampleRate;
        private const float TwoPi = (float)(Math.PI * 2);

        private static readonly float[] SawDetune = { -0.21f, -0.12f, -0.05f, 0f, 0.06f, 0.13f, 0.22f };
        private static readonly float[] SawPan = { -0.9f, 0.6f, -0.35f, 0f, 0.35f, -0.6f, 0.9f };
        private static readonly int[] SawOctave = { 0, 1, 0, 0, 0, 1, 0 };

        private readonly float length;
        private readonly float beat;
        private long frame;
        private uint rng = 0x9E3779B9u;

        private Svf noiseL, noiseR, sawL, sawR, snareL, snareR;
        private readonly double[] sawPhase = new double[SawDetune.Length];
        private double subPhase;

        private double nextHitBeat;
        private float snareEnv, snareDecay, snareToneEnv, snareTonePhase, snareToneFreq, snareGain;
        private float kickTime = -1f;
        private float kickGain;
        private float kickPhase;

        public int SampleRate => MusicEngine.SampleRate;
        public int Channels => 2;

        public BuildupSynth(float lengthSeconds, int seed)
        {
            length = Math.Max(1.5f, lengthSeconds);
            beat = length / 8f;
            rng ^= (uint)seed * 2654435761u;
            var r = new Random(seed);
            for (int i = 0; i < sawPhase.Length; i++) sawPhase[i] = r.NextDouble();
        }

        private float Noise()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388608f - 1f;
        }

        private static float PolyBlep(double t, double dt)
        {
            if (t < dt)
            {
                t /= dt;
                return (float)(t + t - t * t - 1.0);
            }
            if (t > 1.0 - dt)
            {
                t = (t - 1.0) / dt;
                return (float)(t * t + t + t + 1.0);
            }
            return 0f;
        }

        private static double SnareInterval(double beatPos)
        {
            if (beatPos < 4) return 1.0;
            if (beatPos < 6) return 0.5;
            if (beatPos < 7) return 0.25;
            if (beatPos < 8) return 0.125;
            return 0.25; // holding pattern
        }

        private void TriggerHit(double beatPos, float p)
        {
            bool onBeat = Math.Abs(beatPos - Math.Round(beatPos)) < 1e-6;
            bool holding = beatPos >= 8;
            float accent = onBeat ? 1f : 0.78f;
            snareGain = (holding ? 0.4f : 0.08f + 0.6f * (float)Math.Pow(p, 1.2)) * accent;
            snareEnv = 1f;
            snareToneEnv = 1f;
            snareTonePhase = 0f;
            snareToneFreq = 185f * (1f + 0.65f * p);
            float tau = 0.095f * (1f - 0.6f * p);
            snareDecay = (float)Math.Exp(-1.0 / (tau * SR));

            if (onBeat && beatPos < 4)
            {
                kickTime = 0f;
                kickPhase = 0f;
                kickGain = 0.13f + 0.12f * p; // the master bass boost makes these thump plenty
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            int o = offset;
            for (int f = 0; f < frames; f++, frame++)
            {
                float t = frame / SR;
                float p = Math.Min(1f, t / length);
                bool holding = t >= length;
                double beatPos = t / beat;

                // Snare roll scheduler.
                while (beatPos >= nextHitBeat)
                {
                    TriggerHit(nextHitBeat, p);
                    nextHitBeat += SnareInterval(nextHitBeat);
                }

                // 1. Noise sweep: bandpass climbing from 300 Hz to 13 kHz.
                float sweep = holding ? 1f : (float)Math.Pow(p, 1.6);
                float lfo = holding ? 0.82f + 0.18f * (float)Math.Sin(TwoPi * 1.4f * (t - length)) : 1f;
                float cutoff = 300f * (float)Math.Pow(13000f / 300f, sweep) * (holding ? 0.85f + 0.15f * lfo : 1f);
                float res = 0.3f + 0.4f * p;
                noiseL.Process(Noise(), SR, cutoff, res);
                noiseR.Process(Noise(), SR, cutoff * 1.03f, res);
                float noiseGain = (0.02f + 0.9f * (float)Math.Pow(p, 2.0)) * lfo;
                float nl = (noiseL.Band * 0.85f + noiseL.High * 0.25f) * noiseGain;
                float nr = (noiseR.Band * 0.85f + noiseR.High * 0.25f) * noiseGain;

                // 2. Supersaw riser: A1 -> A2 with widening detune and an opening filter.
                float f0 = 55f * (float)Math.Pow(2.0, Math.Pow(p, 1.7));
                if (holding) f0 *= 1f + 0.004f * (float)Math.Sin(TwoPi * 5.5f * t);
                float spread = 0.5f + 0.9f * p;
                float sl = 0f, sr = 0f;
                for (int v = 0; v < sawPhase.Length; v++)
                {
                    double freq = f0 * (1 << SawOctave[v]) * Math.Pow(2.0, SawDetune[v] * spread / 12.0);
                    double dt = freq / SR;
                    sawPhase[v] += dt;
                    if (sawPhase[v] >= 1.0) sawPhase[v] -= 1.0;
                    float s = (float)(2.0 * sawPhase[v] - 1.0) - PolyBlep(sawPhase[v], dt);
                    if (SawOctave[v] == 1) s *= 0.55f;
                    float pan = SawPan[v];
                    sl += s * (1f - Math.Max(0f, pan));
                    sr += s * (1f + Math.Min(0f, pan));
                }
                float sawCut = 160f + 5200f * (float)Math.Pow(p, 2.2);
                sawL.Process(sl, SR, sawCut, 0.25f);
                sawR.Process(sr, SR, sawCut, 0.25f);
                float sawGain = 0.015f + 0.16f * (float)Math.Pow(p, 1.4);
                float syl = sawL.Low * sawGain;
                float syr = sawR.Low * sawGain;

                // 3. Sub sine that follows the riser root.
                subPhase += f0 / SR;
                if (subPhase >= 1) subPhase -= 1;
                float sub = (float)Math.Sin(TwoPi * subPhase) * (0.04f + 0.1f * p);

                // 4. Snare: pitched body + bandpassed noise burst.
                float snare = 0f, snareSideL = 0f, snareSideR = 0f;
                if (snareEnv > 1e-4f)
                {
                    snareTonePhase += snareToneFreq / SR;
                    if (snareTonePhase >= 1f) snareTonePhase -= 1f;
                    float tone = (float)Math.Sin(TwoPi * snareTonePhase) * snareToneEnv * 0.55f;
                    snareToneEnv *= 0.9985f;
                    float snareCut = 1900f * (1f + 1.2f * p);
                    snareL.Process(Noise(), SR, snareCut, 0.2f);
                    snareR.Process(Noise(), SR, snareCut * 1.05f, 0.2f);
                    snare = tone * snareGain * snareEnv;
                    snareSideL = (snareL.Band + snareL.High * 0.4f) * snareGain * snareEnv;
                    snareSideR = (snareR.Band + snareR.High * 0.4f) * snareGain * snareEnv;
                    snareEnv *= snareDecay;
                }

                // 5. Kicks on the first four beats.
                float kick = 0f;
                if (kickTime >= 0f)
                {
                    float kf = 46f + 120f * (float)Math.Exp(-kickTime / 0.032f);
                    kickPhase += kf / SR;
                    if (kickPhase >= 1f) kickPhase -= 1f;
                    float amp = (float)Math.Exp(-kickTime / 0.24f);
                    kick = (float)Math.Tanh(1.6f * Math.Sin(TwoPi * kickPhase)) * amp * kickGain;
                    if (kickTime < 0.003f) kick += Noise() * 0.18f * (1f - kickTime / 0.003f);
                    kickTime += 1f / SR;
                    if (kickTime > 1.2f) kickTime = -1f;
                }

                // 6. Dissonant shimmer (minor second) creeping in near the top for that calamity dread.
                float dread = 0f;
                if (p > 0.45f)
                {
                    float dg = 0.025f * (p - 0.45f) / 0.55f;
                    dread = ((float)Math.Sin(TwoPi * 440f * t) + (float)Math.Sin(TwoPi * 466.16f * t)) * dg
                            * (0.6f + 0.4f * (float)Math.Sin(TwoPi * 7f * t));
                }

                float mono = sub + kick + snare;
                float l = mono + nl + syl + snareSideL + dread;
                float r = mono + nr + syr + snareSideR + dread;

                // Fade in over the first 150 ms so it never starts with a click.
                float fadeIn = Math.Min(1f, t / 0.15f);
                buffer[o++] = (float)Math.Tanh(l * 1.15f) * 0.9f * fadeIn;
                buffer[o++] = (float)Math.Tanh(r * 1.15f) * 0.9f * fadeIn;
            }
            return frames * 2;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// One-shot "drop" hit layered on top of the boss track: pitch-dropping sub boom, a short
    /// click, a bright crash and a dark inharmonic gong tail. About three seconds long.
    /// </summary>
    internal sealed class ImpactSource : ISampleSource
    {
        private const float SR = MusicEngine.SampleRate;
        private const float TwoPi = (float)(Math.PI * 2);
        private static readonly float[] GongFreqs = { 110f, 155.6f, 233.1f, 311.1f, 466.2f };
        private static readonly float[] GongDecay = { 1.6f, 1.2f, 0.9f, 0.7f, 0.5f };

        private readonly float strength;
        private readonly int totalFrames;
        private long frame;
        private float subPhase;
        private uint rng = 0x1234567u;
        private readonly Biquad crashHpL = new Biquad(), crashHpR = new Biquad();

        public int SampleRate => MusicEngine.SampleRate;
        public int Channels => 2;

        public ImpactSource(float strength)
        {
            this.strength = strength;
            totalFrames = (int)(SR * 3.2f);
            crashHpL.SetHighPass(SR, 2600f, 0.7f);
            crashHpR.SetHighPass(SR, 2800f, 0.7f);
        }

        private float Noise()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388608f - 1f;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = Math.Min(count / 2, (int)(totalFrames - frame));
            if (frames <= 0) return 0;
            int o = offset;
            for (int f = 0; f < frames; f++, frame++)
            {
                float t = frame / SR;
                float attack = Math.Min(1f, t / 0.003f);

                float freq = 40f + 125f * (float)Math.Exp(-t / 0.055f);
                subPhase += freq / SR;
                if (subPhase >= 1f) subPhase -= 1f;
                float sub = (float)Math.Tanh(2.2f * Math.Sin(TwoPi * subPhase)) * 0.62f * (float)Math.Exp(-t / 1.05f) * attack;
                float body = (float)Math.Sin(TwoPi * 82f * t) * 0.3f * (float)Math.Exp(-t / 0.16f) * attack;
                float click = t < 0.006f ? Noise() * 0.35f * (1f - t / 0.006f) : 0f;

                float crashEnv = 0.2f * (float)Math.Exp(-t / 0.7f) * attack;
                float cl = crashHpL.Process(Noise()) * crashEnv;
                float cr = crashHpR.Process(Noise()) * crashEnv;

                float gong = 0f;
                for (int i = 0; i < GongFreqs.Length; i++)
                    gong += (float)Math.Sin(TwoPi * GongFreqs[i] * t + i) * (float)Math.Exp(-t / GongDecay[i]);
                gong *= 0.035f * attack;

                // Fade out the last 200 ms.
                float tail = Math.Min(1f, (totalFrames - frame) / (SR * 0.2f));
                float mono = (sub + body + click + gong) * strength * tail;
                buffer[o++] = mono + cl * strength * tail;
                buffer[o++] = mono + cr * strength * tail;
            }
            return frames * 2;
        }

        public void Dispose() { }
    }
}
