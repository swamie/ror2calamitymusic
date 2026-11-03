using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CalamityMusic.Library;

namespace CalamityMusic.Audio
{
    internal enum EngineEventKind
    {
        TrackStarted,
        TrackEnding,
        TrackEnded,
        LoadFailed,
        BuildupReady,
        BuildupFailed,
    }

    internal struct EngineEvent
    {
        public EngineEventKind Kind;
        public int VoiceId;
        public TrackInfo Track;
        public string Message;
        public double Seconds;
    }

    /// <summary>
    /// The mixer. Owns every playing sound ("voice"), handles fades/crossfades and the timed drop,
    /// and runs the master chain: bass boost -> pause muffle -> volume -> look-ahead limiter.
    /// The main thread only queues commands; all state below is touched by the audio thread alone.
    /// </summary>
    internal sealed class MusicEngine : IDisposable
    {
        public const int SampleRate = 48000;
        private const int FramesPerBuffer = 2400;   // 50 ms
        private const int BufferCount = 5;          // 250 ms queued, rides out GC pauses
        private const float CrossfadeLeadSeconds = 3f;

        private enum VoiceKind { Music, Buildup, OneShot }

        private sealed class Voice
        {
            public int Id;
            public VoiceKind Kind;
            public ISampleSource Source;
            public TrackReader Track;
            public TrackInfo Info;
            public float Fade;
            public float FadeStep;
            public float Gain = 1f;
            public int StartDelay;
            public bool Started;
            public bool Finished;
            public bool EndingNotified;
            public int LastLoopCount;
            public int FramesThisBlock;
            public Voice Next; // gapless follow-up (next part of a multi-part song)
            public float[] Scratch = new float[FramesPerBuffer * 2];
        }

        private readonly ConcurrentQueue<Action> commands = new ConcurrentQueue<Action>();
        public readonly ConcurrentQueue<EngineEvent> Events = new ConcurrentQueue<EngineEvent>();

        private readonly List<Voice> voices = new List<Voice>();
        private readonly WaveOutPlayer player;
        private float[] mix = new float[FramesPerBuffer * 2];

        private readonly Biquad shelfL = new Biquad(), shelfR = new Biquad();
        private readonly Biquad punchL = new Biquad(), punchR = new Biquad();
        private readonly Biquad muffleL = new Biquad(), muffleR = new Biquad();
        private readonly Limiter limiter = new Limiter(SampleRate);
        private float appliedBassDb = float.NaN, appliedBassFreq = float.NaN, bassCompensation = 1f;
        private float currentVolume, currentMuffle, appliedMuffleCut = -1f;
        private float bassLp, bassEnv, levelEnv;
        private int idleFrames;
        private int nextVoiceId = 1;
        private int generation;

        // Written by the main thread, read by the audio thread.
        private volatile float targetVolume = 0.7f;
        private volatile float bassBoostDb = 6f;
        private volatile float bassFrequency = 90f;
        private volatile float muffle;
        private volatile float lowHealth;
        private float currentLow;
        private long heartFrame;
        private float heartPhaseL, heartPhaseD;

        // Written by the audio thread, read by the main thread.
        private volatile float bassLevel;
        private volatile float level;
        private volatile int activeMusicVoice;
        private volatile bool anythingPlaying;

        private bool started;

        public bool Available => started && player.Healthy;
        public float BassLevel => bassLevel;
        public float Level => level;
        public bool AnythingPlaying => anythingPlaying;
        public int ActiveMusicVoice => activeMusicVoice;

        public float Volume { set => targetVolume = Math.Max(0f, Math.Min(1.5f, value)); }
        public float BassBoostDb { set => bassBoostDb = Math.Max(0f, Math.Min(18f, value)); }
        public float BassFrequency { set => bassFrequency = Math.Max(30f, Math.Min(250f, value)); }
        public float Muffle { set => muffle = Math.Max(0f, Math.Min(1f, value)); }

        /// <summary>0..1: how close to death the player is. Muffles the music and adds a heartbeat.</summary>
        public float LowHealth { set => lowHealth = Math.Max(0f, Math.Min(1f, value)); }

        public MusicEngine()
        {
            player = new WaveOutPlayer(SampleRate, FramesPerBuffer, BufferCount, Render);
        }

        public void Start()
        {
            try
            {
                player.Start();
                started = true;
            }
            catch (Exception e)
            {
                started = false;
                Log.Error("Could not open an audio output device, Calamity music is disabled: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ main-thread API

        /// <summary>Starts the teleporter buildup. Any playing music is faded out underneath it.</summary>
        public void StartBuildup(ISampleSource source)
        {
            Interlocked.Increment(ref generation); // cancels any song still loading
            commands.Enqueue(() =>
            {
                FadeOutKinds(0.35f, VoiceKind.Music, VoiceKind.Buildup);
                var v = NewVoice(VoiceKind.Buildup, PrepareSource(source));
                v.FadeStep = 1f / (SampleRate * 0.12f);
                voices.Add(v);
            });
        }

        /// <summary>
        /// Starts a custom buildup file. It is opened in the background (an MP3's length can take a
        /// moment to work out); BuildupReady reports its length, BuildupFailed means use the synth.
        /// </summary>
        public void StartBuildupFile(string path)
        {
            int gen = Interlocked.Increment(ref generation);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                TrackReader reader;
                try
                {
                    reader = new TrackReader(AudioDecoder.Open(path), 0, 0, 0f, false);
                }
                catch (Exception e)
                {
                    if (gen == Volatile.Read(ref generation))
                        Events.Enqueue(new EngineEvent { Kind = EngineEventKind.BuildupFailed, Message = path + ": " + e.Message });
                    return;
                }
                double length = reader.LengthSeconds;
                var source = new PrebufferedSource(PrepareSource(reader), SampleRate / 4);
                commands.Enqueue(() =>
                {
                    if (gen != Volatile.Read(ref generation))
                    {
                        source.Dispose();
                        return;
                    }
                    FadeOutKinds(0.35f, VoiceKind.Music, VoiceKind.Buildup);
                    var v = NewVoice(VoiceKind.Buildup, source);
                    v.FadeStep = 1f / (SampleRate * 0.05f);
                    voices.Add(v);
                    Events.Enqueue(new EngineEvent { Kind = EngineEventKind.BuildupReady, Seconds = length });
                });
            });
        }

        /// <summary>Opens a song file in the background and schedules the given transition once it is ready.</summary>
        public void Play(TrackInfo track, bool loop, Transition transition)
        {
            int gen = Interlocked.Increment(ref generation);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Voice v;
                try
                {
                    v = OpenTrack(track, loop);
                }
                catch (Exception e)
                {
                    // A load that was superseded in the meantime doesn't matter any more.
                    if (gen == Volatile.Read(ref generation))
                        Events.Enqueue(new EngineEvent { Kind = EngineEventKind.LoadFailed, Track = track, Message = e.Message });
                    return;
                }

                commands.Enqueue(() =>
                {
                    if (gen != Volatile.Read(ref generation))
                    {
                        v.Source.Dispose();
                        return;
                    }
                    ApplyTransition(v, transition);
                });
            });
        }

        /// <summary>Fades everything out. Pending loads are cancelled.</summary>
        public void StopAll(float fadeSeconds)
        {
            Interlocked.Increment(ref generation);
            commands.Enqueue(() =>
            {
                if (TapeActive)
                {
                    // Stopping while the tape is slowed/stopped: drop everything now and reset the speed.
                    foreach (var v in voices) v.Finished = true;
                    TapeReset();
                    return;
                }
                FadeOutKinds(fadeSeconds, VoiceKind.Music, VoiceKind.Buildup, VoiceKind.OneShot);
            });
        }

        // ------------------------------------------------------------------ audio-thread internals

        private Voice NewVoice(VoiceKind kind, ISampleSource source)
        {
            return new Voice { Id = Interlocked.Increment(ref nextVoiceId), Kind = kind, Source = source };
        }

        private static ISampleSource PrepareSource(ISampleSource source)
        {
            return source.SampleRate == SampleRate ? source : new SincResampler(source, SampleRate);
        }

        private Voice OpenTrack(TrackInfo info, bool loop)
        {
            var file = AudioDecoder.Open(info.Path);
            if (file.LengthSeconds < 1.0)
            {
                // Usually a broken download or a web page saved with a .mp3 name.
                file.Dispose();
                throw new InvalidDataException($"only {file.LengthSeconds:0.00}s of audio, not a usable song");
            }
            var reader = new TrackReader(file, info.StartSeconds, info.LoopStartSeconds, info.GainDb, loop);
            var source = new PrebufferedSource(PrepareSource(reader), SampleRate / 4);
            return new Voice { Id = Interlocked.Increment(ref nextVoiceId), Kind = VoiceKind.Music, Source = source, Track = reader, Info = info };
        }

        private void ApplyTransition(Voice v, Transition t)
        {
            switch (t.Kind)
            {
                case TransitionKind.Drop:
                {
                    // Cut the buildup, leave a short breath of silence, then slam in the song and the impact.
                    FadeOutKinds(0.045f, VoiceKind.Buildup, VoiceKind.Music);
                    // A drop replacing one still in its gap takes over its impact too, so it never hits twice.
                    foreach (var other in voices)
                        if (other.Kind == VoiceKind.OneShot && !other.Started && other.StartDelay > 0) other.Finished = true;
                    int delay = (int)(SampleRate * t.GapSeconds);
                    v.StartDelay = delay;
                    v.Fade = 0f;
                    v.FadeStep = 1f / (SampleRate * 0.004f);
                    voices.Add(v);
                    if (t.ImpactStrength > 0f)
                    {
                        var impact = NewVoice(VoiceKind.OneShot, new ImpactSource(t.ImpactStrength));
                        impact.StartDelay = delay;
                        impact.Fade = 1f;
                        voices.Add(impact);
                    }
                    break;
                }
                case TransitionKind.Append:
                {
                    // Start exactly when the current song ends (sample-accurate, no gap, no fade).
                    Voice current = null;
                    foreach (var other in voices)
                        if (other.Kind == VoiceKind.Music && other.Started && !other.Finished && other.FadeStep >= 0 && other.Next == null)
                            current = other;
                    v.Fade = 1f;
                    v.FadeStep = 0f;
                    if (current != null) current.Next = v;
                    else
                    {
                        v.Fade = 0f;
                        v.FadeStep = 1f / (SampleRate * 0.01f);
                        voices.Add(v);
                    }
                    break;
                }
                case TransitionKind.Crossfade:
                {
                    FadeOutKinds(t.FadeSeconds, VoiceKind.Music, VoiceKind.Buildup);
                    v.Fade = 0f;
                    v.FadeStep = 1f / (SampleRate * Math.Max(0.01f, t.FadeSeconds));
                    voices.Add(v);
                    break;
                }
                default:
                {
                    FadeOutKinds(0.3f, VoiceKind.Music, VoiceKind.Buildup);
                    v.Fade = 0f;
                    v.FadeStep = 1f / (SampleRate * Math.Max(0.01f, t.FadeSeconds));
                    voices.Add(v);
                    break;
                }
            }
        }

        private void FadeOutKinds(float seconds, params VoiceKind[] kinds)
        {
            float step = -1f / (SampleRate * Math.Max(0.005f, seconds));
            foreach (var v in voices)
            {
                if (Array.IndexOf(kinds, v.Kind) < 0) continue;
                if (!v.Started && v.StartDelay > 0)
                {
                    v.Finished = true; // never got going, just drop it
                    continue;
                }
                if (v.FadeStep > step) v.FadeStep = step;
            }
        }

        /// <summary>Runs the mixer without an audio device (used by the offline test harness).</summary>
        internal void RenderOffline(float[] output, int frames) => Render(output, frames);

        private void Render(float[] output, int frames)
        {
            while (commands.TryDequeue(out var cmd))
            {
                try { cmd(); }
                catch (Exception e) { Log.Error("Audio command failed: " + e); }
            }

            int samples = frames * 2;
            bool tape = TapeActive;
            if (tape) TapeRender(frames);
            else MixVoices(frames);

            if (voices.Count == 0 && !tape) idleFrames += frames; else idleFrames = 0;
            if (idleFrames > SampleRate * 2)
            {
                // Nothing has played for a while: skip the DSP entirely.
                Array.Clear(output, 0, samples);
                bassLevel = 0f;
                level = 0f;
                return;
            }

            UpdateFilters();
            MasterChain(output, frames);
        }

        /// <summary>Mixes <paramref name="frames"/> frames of every voice into <see cref="mix"/>.</summary>
        private void MixVoices(int frames)
        {
            int samples = frames * 2;
            if (mix.Length < samples) mix = new float[samples];
            Array.Clear(mix, 0, samples);
            if (frames == 0) return;

            int music = 0;
            for (int i = 0; i < voices.Count; i++)
            {
                var v = voices[i];
                if (v.Finished)
                {
                    // Stopped from outside (StopAll during a tape stop, a dropped pending voice): never mix it again.
                    v.FramesThisBlock = -1;
                    continue;
                }
                try
                {
                    MixVoice(v, frames);
                }
                catch (Exception e)
                {
                    // A broken/locked file must not take the whole mixer down: drop just this voice.
                    v.Finished = true;
                    v.FramesThisBlock = -1;
                    Log.Error("Playback error in " + (v.Info?.Path ?? v.Kind.ToString()) + ": " + e.Message);
                    Events.Enqueue(new EngineEvent { Kind = EngineEventKind.LoadFailed, VoiceId = v.Id, Track = v.Info, Message = e.Message });
                }
                if (v.Kind == VoiceKind.Music && !v.Finished && v.FadeStep >= 0 && v.Started) music = v.Id;

                // Ran out naturally with a follow-up queued: hand over inside this same block.
                if (v.Finished && v.Next != null && v.Track != null && v.Track.Ended && v.FramesThisBlock >= 0)
                {
                    var next = v.Next;
                    v.Next = null;
                    next.StartDelay = Math.Min(frames, v.FramesThisBlock);
                    voices.Add(next); // mixed later in this loop
                }
            }
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var v = voices[i];
                if (!v.Finished) continue;
                if (v.Kind == VoiceKind.Music && v.Track != null && v.Track.Ended)
                    Events.Enqueue(new EngineEvent { Kind = EngineEventKind.TrackEnded, VoiceId = v.Id, Track = v.Info });
                if (v.Next != null)
                {
                    // Faded out before reaching its end: the queued follow-up never plays.
                    try { v.Next.Source.Dispose(); } catch { /* ignore */ }
                    v.Next = null;
                }
                try { v.Source.Dispose(); } catch { /* already closed */ }
                voices.RemoveAt(i);
            }
            activeMusicVoice = music;
            anythingPlaying = voices.Count > 0;
        }

        // ------------------------------------------------------------------ tape stop (varispeed)

        private float tapeRate = 1f, tapeTarget = 1f, tapeStep;
        private float[] tapeBuf = new float[(FramesPerBuffer + 8) * 2];
        private float[] tapeRates = new float[FramesPerBuffer];
        private float[] tapeOut = new float[FramesPerBuffer * 2];
        private int tapeBufFrames;
        private double tapePos;

        private bool TapeActive => tapeRate < 0.9999f || tapeTarget < 1f;

        /// <summary>Slows everything down to a halt like a stopped tape (pitch drops with it).</summary>
        public void TapeStop(float seconds) => commands.Enqueue(() =>
        {
            tapeTarget = 0f;
            tapeStep = 1f / (SampleRate * Math.Max(0.05f, seconds));
        });

        /// <summary>Spins the tape back up to normal speed.</summary>
        public void TapeStart(float seconds) => commands.Enqueue(() =>
        {
            tapeTarget = 1f;
            tapeStep = 1f / (SampleRate * Math.Max(0.05f, seconds));
        });

        private void TapeReset()
        {
            tapeRate = tapeTarget = 1f;
            tapeBufFrames = 0;
            tapePos = 0;
        }

        private void TapeRender(int frames)
        {
            if (tapeRates.Length < frames) tapeRates = new float[frames];
            if (tapeOut.Length < frames * 2) tapeOut = new float[frames * 2];

            // Speed for every output frame (eased so it starts and ends smoothly).
            double total = 0;
            float r = tapeRate;
            for (int i = 0; i < frames; i++)
            {
                if (r != tapeTarget) r = r < tapeTarget ? Math.Min(tapeTarget, r + tapeStep) : Math.Max(tapeTarget, r - tapeStep);
                float eff = r * r * (3f - 2f * r);
                tapeRates[i] = eff;
                total += eff;
            }
            tapeRate = r;

            // Pull just as much new audio as the slowed playback needs.
            int need = (int)Math.Floor(tapePos + total) + 2;
            if (need > tapeBufFrames)
            {
                int add = need - tapeBufFrames;
                MixVoices(add);
                if (tapeBuf.Length < need * 2)
                {
                    var bigger = new float[need * 2 + 64];
                    Array.Copy(tapeBuf, bigger, tapeBufFrames * 2);
                    tapeBuf = bigger;
                }
                Array.Copy(mix, 0, tapeBuf, tapeBufFrames * 2, add * 2);
                tapeBufFrames += add;
            }

            for (int i = 0, o = 0; i < frames; i++, o += 2)
            {
                int k = (int)tapePos;
                float f = (float)(tapePos - k);
                int a = k * 2, b = Math.Min(k + 1, tapeBufFrames - 1) * 2;
                float gain = Math.Min(1f, tapeRates[i] * 4f); // fade the last subsonic bit instead of thumping
                tapeOut[o] = (tapeBuf[a] + (tapeBuf[b] - tapeBuf[a]) * f) * gain;
                tapeOut[o + 1] = (tapeBuf[a + 1] + (tapeBuf[b + 1] - tapeBuf[a + 1]) * f) * gain;
                tapePos += tapeRates[i];
            }

            int consumed = Math.Min((int)Math.Floor(tapePos), tapeBufFrames);
            if (consumed > 0)
            {
                Array.Copy(tapeBuf, consumed * 2, tapeBuf, 0, (tapeBufFrames - consumed) * 2);
                tapeBufFrames -= consumed;
                tapePos -= consumed;
            }

            if (mix.Length < frames * 2) mix = new float[frames * 2];
            Array.Copy(tapeOut, mix, frames * 2);
            if (!TapeActive) TapeReset(); // back to full speed: hand over to direct mixing
        }

        /// <summary>Plays the drop impact on its own (final-phase hits).</summary>
        public void PlayImpact(float strength) => commands.Enqueue(() =>
        {
            var v = NewVoice(VoiceKind.OneShot, new ImpactSource(strength));
            v.Fade = 1f;
            voices.Add(v);
        });

        private void MixVoice(Voice v, int frames)
        {
            v.FramesThisBlock = 0;
            int offset = 0;
            if (v.StartDelay > 0)
            {
                int skip = Math.Min(frames, v.StartDelay);
                v.StartDelay -= skip;
                offset = skip;
                if (offset >= frames) return;
            }
            if (!v.Started)
            {
                v.Started = true;
                if (v.Kind == VoiceKind.Music)
                    Events.Enqueue(new EngineEvent { Kind = EngineEventKind.TrackStarted, VoiceId = v.Id, Track = v.Info });
            }

            int want = (frames - offset) * 2;
            if (v.Scratch.Length < want) v.Scratch = new float[want];
            int got = 0;
            int guard = 0;
            while (got < want && guard++ < 8)
            {
                int n = v.Source.Read(v.Scratch, got, want - got);
                if (n <= 0) break;
                got += n;
            }
            v.FramesThisBlock = offset + got / 2;
            if (got < want)
            {
                Array.Clear(v.Scratch, got, want - got);
                v.Finished = true;
            }

            const float HalfPi = (float)(Math.PI / 2);
            int m = offset * 2;
            for (int i = 0; i < want; i += 2)
            {
                if (v.FadeStep != 0f)
                {
                    v.Fade += v.FadeStep;
                    if (v.Fade >= 1f) { v.Fade = 1f; v.FadeStep = 0f; }
                    else if (v.Fade <= 0f) { v.Fade = 0f; v.Finished = true; v.FadeStep = 0f; }
                }
                // Equal-power curve so crossfades don't dip in the middle.
                float g = (float)Math.Sin(v.Fade * HalfPi) * v.Gain;
                mix[m++] += v.Scratch[i] * g;
                mix[m++] += v.Scratch[i + 1] * g;
            }

            // Ask the director for the next song a few seconds before a non-looping track ends.
            if (v.Kind == VoiceKind.Music && v.Track != null && !v.Track.Loop && !v.EndingNotified && v.FadeStep >= 0
                && v.Track.RemainingSeconds < CrossfadeLeadSeconds)
            {
                v.EndingNotified = true;
                Events.Enqueue(new EngineEvent { Kind = EngineEventKind.TrackEnding, VoiceId = v.Id, Track = v.Info });
            }
            if (v.Track != null && v.Track.LoopCount != v.LastLoopCount)
            {
                v.LastLoopCount = v.Track.LoopCount;
                if (v.Kind == VoiceKind.Music)
                    Events.Enqueue(new EngineEvent { Kind = EngineEventKind.TrackStarted, VoiceId = v.Id, Track = v.Info, Message = "loop" });
            }
        }

        private void UpdateFilters()
        {
            float db = bassBoostDb, freq = bassFrequency;
            if (db != appliedBassDb || freq != appliedBassFreq)
            {
                appliedBassDb = db;
                appliedBassFreq = freq;
                // Broad low shelf for weight plus a peak an octave lower for sub "thump".
                shelfL.SetLowShelf(SampleRate, freq, 0.8f, db);
                shelfR.SetLowShelf(SampleRate, freq, 0.8f, db);
                punchL.SetPeaking(SampleRate, freq * 0.55f, 0.9f, db * 0.45f);
                punchR.SetPeaking(SampleRate, freq * 0.55f, 0.9f, db * 0.45f);
                // Pull the overall level down a bit so the limiter isn't working flat out.
                bassCompensation = (float)Math.Pow(10, -db * 0.4 / 20.0);
            }
        }

        private void MasterChain(float[] output, int frames)
        {
            float volTarget = targetVolume;
            float lowCoef = 1f - (float)Math.Exp(-1.0 / (SampleRate * 0.3));
            float lowTarget = voices.Count > 0 ? lowHealth : 0f;
            float muffleTarget = Math.Max(muffle, lowTarget * 0.5f);
            const float TwoPi = (float)(Math.PI * 2);
            float volCoef = 1f - (float)Math.Exp(-1.0 / (SampleRate * 0.03));
            float muffleCoef = 1f - (float)Math.Exp(-1.0 / (SampleRate * 0.15));
            float bassLpCoef = 1f - (float)Math.Exp(-2 * Math.PI * 150.0 / SampleRate);
            float envAttack = 1f - (float)Math.Exp(-1.0 / (SampleRate * 0.006));
            float envRelease = 1f - (float)Math.Exp(-1.0 / (SampleRate * 0.16));
            bool boost = appliedBassDb > 0.05f;

            for (int f = 0, i = 0; f < frames; f++, i += 2)
            {
                float l = mix[i], r = mix[i + 1];

                if (boost)
                {
                    l = punchL.Process(shelfL.Process(l)) * bassCompensation;
                    r = punchR.Process(shelfR.Process(r)) * bassCompensation;
                }

                currentMuffle += (muffleTarget - currentMuffle) * muffleCoef;
                if (currentMuffle > 0.002f)
                {
                    if ((f & 63) == 0)
                    {
                        float cut = 18000f * (float)Math.Pow(800f / 18000f, Math.Sqrt(currentMuffle));
                        if (Math.Abs(cut - appliedMuffleCut) > 1f)
                        {
                            appliedMuffleCut = cut;
                            muffleL.SetLowPass(SampleRate, cut, 0.707f);
                            muffleR.SetLowPass(SampleRate, cut, 0.707f);
                        }
                    }
                    l = muffleL.Process(l);
                    r = muffleR.Process(r);
                }
                else if (appliedMuffleCut >= 0f)
                {
                    appliedMuffleCut = -1f;
                    muffleL.Reset();
                    muffleR.Reset();
                }

                // Low health: a "lub-dub" heartbeat that speeds up the closer the player is to death.
                currentLow += (lowTarget - currentLow) * lowCoef;
                if (currentLow > 0.01f)
                {
                    float bpm = 66f + 54f * currentLow;
                    long period = (long)(SampleRate * 60f / bpm);
                    // Position inside the current beat; the period changes as health does, so wrap instead of using modulo.
                    if (heartFrame >= period) heartFrame -= period;
                    if (heartFrame >= period) heartFrame = 0;
                    long t = heartFrame;
                    float thump = 0f;
                    float ts = t / (float)SampleRate;
                    if (ts < 0.2f)
                    {
                        heartPhaseL += (44f + 30f * (float)Math.Exp(-ts / 0.03f)) / SampleRate;
                        thump += (float)Math.Sin(TwoPi * heartPhaseL) * (float)Math.Exp(-ts / 0.07f);
                    }
                    else heartPhaseL = 0f;
                    float td = ts - 0.24f;
                    if (td >= 0f && td < 0.2f)
                    {
                        heartPhaseD += (40f + 24f * (float)Math.Exp(-td / 0.03f)) / SampleRate;
                        thump += 0.7f * (float)Math.Sin(TwoPi * heartPhaseD) * (float)Math.Exp(-td / 0.06f);
                    }
                    else heartPhaseD = 0f;
                    thump *= currentLow * 0.55f;
                    l += thump;
                    r += thump;
                    heartFrame++;
                }
                else heartFrame = 0;

                // Analysis for the Now Playing glow (taken before volume so it reacts at any volume).
                float mono = (l + r) * 0.5f;
                bassLp += (mono - bassLp) * bassLpCoef;
                float b = Math.Abs(bassLp);
                bassEnv += (b - bassEnv) * (b > bassEnv ? envAttack : envRelease);
                float a = Math.Abs(mono);
                levelEnv += (a - levelEnv) * (a > levelEnv ? envAttack : envRelease);

                currentVolume += (volTarget * (1f - 0.4f * currentMuffle) - currentVolume) * volCoef;
                l *= currentVolume;
                r *= currentVolume;
                limiter.Process(ref l, ref r);
                output[i] = l;
                output[i + 1] = r;
            }

            bassLevel = Math.Min(1f, bassEnv * 2.2f);
            level = Math.Min(1f, levelEnv * 2f);
        }

        public void Dispose()
        {
            if (!player.Stop()) return; // audio thread still busy; don't touch its voices
            foreach (var v in voices)
            {
                try { v.Source.Dispose(); } catch { /* ignore */ }
            }
            voices.Clear();
        }
    }

    internal enum TransitionKind
    {
        FadeIn,
        Drop,
        Crossfade,
        Append,
    }

    internal struct Transition
    {
        public TransitionKind Kind;
        public float FadeSeconds;
        public float GapSeconds;
        public float ImpactStrength;

        public static Transition Drop(float impactStrength, float gapSeconds = 0.09f) =>
            new Transition { Kind = TransitionKind.Drop, ImpactStrength = impactStrength, GapSeconds = gapSeconds };

        public static Transition Crossfade(float seconds) =>
            new Transition { Kind = TransitionKind.Crossfade, FadeSeconds = seconds };

        public static Transition FadeIn(float seconds) =>
            new Transition { Kind = TransitionKind.FadeIn, FadeSeconds = seconds };

        /// <summary>Plays right after the current song ends, with no gap (for multi-part songs).</summary>
        public static Transition Append() => new Transition { Kind = TransitionKind.Append };
    }

    /// <summary>Decodes the first chunk of a song ahead of time so the drop starts instantly.</summary>
    internal sealed class PrebufferedSource : ISampleSource
    {
        private readonly ISampleSource inner;
        private readonly float[] pre;
        private readonly int preCount;
        private int prePos;

        public PrebufferedSource(ISampleSource inner, int frames)
        {
            this.inner = inner;
            pre = new float[frames * inner.Channels];
            int got = 0;
            while (got < pre.Length)
            {
                int n = inner.Read(pre, got, pre.Length - got);
                if (n <= 0) break;
                got += n;
            }
            preCount = got;
        }

        public int SampleRate => inner.SampleRate;
        public int Channels => inner.Channels;

        public int Read(float[] buffer, int offset, int count)
        {
            int written = 0;
            if (prePos < preCount)
            {
                int n = Math.Min(count, preCount - prePos);
                Array.Copy(pre, prePos, buffer, offset, n);
                prePos += n;
                written = n;
            }
            if (written < count)
            {
                int n = inner.Read(buffer, offset + written, count - written);
                if (n > 0) written += n;
            }
            return written;
        }

        public void Dispose() => inner.Dispose();
    }
}
