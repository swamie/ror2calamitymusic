using System;

namespace CalamityMusic.Audio
{
    /// <summary>RBJ cookbook biquad, transposed direct form II.</summary>
    internal sealed class Biquad
    {
        private float b0 = 1, b1, b2, a1, a2;
        private float z1, z2;

        public void SetLowShelf(float sampleRate, float freq, float slope, float dbGain)
        {
            double A = Math.Pow(10, dbGain / 40.0);
            double w0 = 2 * Math.PI * freq / sampleRate;
            double cos = Math.Cos(w0), sin = Math.Sin(w0);
            double alpha = sin / 2 * Math.Sqrt((A + 1 / A) * (1 / slope - 1) + 2);
            double sqA = 2 * Math.Sqrt(A) * alpha;
            double a0 = (A + 1) + (A - 1) * cos + sqA;
            Set((A * ((A + 1) - (A - 1) * cos + sqA)) / a0,
                (2 * A * ((A - 1) - (A + 1) * cos)) / a0,
                (A * ((A + 1) - (A - 1) * cos - sqA)) / a0,
                (-2 * ((A - 1) + (A + 1) * cos)) / a0,
                ((A + 1) + (A - 1) * cos - sqA) / a0);
        }

        public void SetPeaking(float sampleRate, float freq, float q, float dbGain)
        {
            double A = Math.Pow(10, dbGain / 40.0);
            double w0 = 2 * Math.PI * freq / sampleRate;
            double alpha = Math.Sin(w0) / (2 * q);
            double cos = Math.Cos(w0);
            double a0 = 1 + alpha / A;
            Set((1 + alpha * A) / a0, (-2 * cos) / a0, (1 - alpha * A) / a0, (-2 * cos) / a0, (1 - alpha / A) / a0);
        }

        public void SetLowPass(float sampleRate, float freq, float q)
        {
            freq = Math.Min(freq, sampleRate * 0.49f);
            double w0 = 2 * Math.PI * freq / sampleRate;
            double alpha = Math.Sin(w0) / (2 * q);
            double cos = Math.Cos(w0);
            double a0 = 1 + alpha;
            Set(((1 - cos) / 2) / a0, (1 - cos) / a0, ((1 - cos) / 2) / a0, (-2 * cos) / a0, (1 - alpha) / a0);
        }

        public void SetHighPass(float sampleRate, float freq, float q)
        {
            double w0 = 2 * Math.PI * freq / sampleRate;
            double alpha = Math.Sin(w0) / (2 * q);
            double cos = Math.Cos(w0);
            double a0 = 1 + alpha;
            Set(((1 + cos) / 2) / a0, (-(1 + cos)) / a0, ((1 + cos) / 2) / a0, (-2 * cos) / a0, (1 - alpha) / a0);
        }

        private void Set(double nb0, double nb1, double nb2, double na1, double na2)
        {
            b0 = (float)nb0; b1 = (float)nb1; b2 = (float)nb2; a1 = (float)na1; a2 = (float)na2;
        }

        public float Process(float x)
        {
            float y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }

        public void Reset() { z1 = z2 = 0; }
    }

    /// <summary>
    /// Zero-delay-feedback state variable filter (Cytomic / Simper). Used by the buildup synth
    /// because it stays stable while the cutoff is swept every sample.
    /// </summary>
    internal struct Svf
    {
        private float ic1, ic2;
        public float Low, Band, High;

        public void Process(float x, float sampleRate, float cutoff, float resonance)
        {
            cutoff = Math.Max(20f, Math.Min(cutoff, sampleRate * 0.45f));
            float g = (float)Math.Tan(Math.PI * cutoff / sampleRate);
            float k = 2f - 2f * Math.Max(0f, Math.Min(resonance, 0.98f));
            float a1 = 1f / (1f + g * (g + k));
            float a2 = g * a1;
            float a3 = g * a2;
            float v3 = x - ic2;
            float v1 = a1 * ic1 + a2 * v3;
            float v2 = ic2 + a2 * ic1 + a3 * v3;
            ic1 = 2 * v1 - ic1;
            ic2 = 2 * v2 - ic2;
            Low = v2;
            Band = v1;
            High = x - k * v1 - v2;
        }
    }

    /// <summary>
    /// Stereo look-ahead peak limiter. A sliding-window minimum over the look-ahead finds the gain
    /// each sample needs, then the gain is smoothed so it is fully applied before the peak leaves the
    /// delay line. Keeps the bass boost loud without ever clipping the output.
    /// </summary>
    internal sealed class Limiter
    {
        private readonly int lookahead;
        private readonly float[] delayL, delayR;
        private readonly float[] dqGain;
        private readonly long[] dqIdx;
        private readonly int cap;
        private int head, count;
        private int pos;
        private long n;
        private float gain = 1f;
        private readonly float attackCoef, releaseCoef;

        public float Ceiling = 0.97f;
        public float CurrentGain => gain;

        public Limiter(int sampleRate, float lookaheadMs = 3f, float releaseMs = 120f)
        {
            lookahead = Math.Max(8, (int)(sampleRate * lookaheadMs / 1000f));
            delayL = new float[lookahead];
            delayR = new float[lookahead];
            cap = lookahead + 2;
            dqGain = new float[cap];
            dqIdx = new long[cap];
            attackCoef = 1f - (float)Math.Exp(-5.0 / lookahead);
            releaseCoef = 1f - (float)Math.Exp(-1.0 / (sampleRate * releaseMs / 1000f));
        }

        public void Process(ref float l, ref float r)
        {
            float peak = Math.Max(Math.Abs(l), Math.Abs(r));
            float need = peak > Ceiling ? Ceiling / peak : 1f;

            // Monotonic deque holding the minimum required gain over the look-ahead window.
            while (count > 0 && dqGain[(head + count - 1) % cap] >= need) count--;
            int tail = (head + count) % cap;
            dqGain[tail] = need;
            dqIdx[tail] = n;
            count++;
            while (dqIdx[head] < n - lookahead)
            {
                head = (head + 1) % cap;
                count--;
            }
            float target = dqGain[head];

            gain += (target - gain) * (target < gain ? attackCoef : releaseCoef);
            if (gain > 1f) gain = 1f;

            float dl = delayL[pos], dr = delayR[pos];
            delayL[pos] = l;
            delayR[pos] = r;
            pos = (pos + 1) % lookahead;
            n++;

            l = Clamp(dl * gain);
            r = Clamp(dr * gain);
        }

        private float Clamp(float x) => x > Ceiling ? Ceiling : (x < -Ceiling ? -Ceiling : x);
    }
}
