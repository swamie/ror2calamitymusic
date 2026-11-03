using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CalamityMusic.Audio
{
    /// <summary>
    /// Plays audio through the Windows waveOut API on its own thread. Risk of Rain 2 routes all of its
    /// audio through Wwise and has Unity's audio engine switched off, so custom music needs its own
    /// output. waveOut is available on every Windows install and works under Proton/Wine too.
    /// Buffers live in unmanaged memory so the garbage collector can never move them mid-playback.
    /// If the device stops taking buffers (headset unplugged, default device changed) it is closed
    /// and reopened; while that fails, <see cref="Healthy"/> is false so the mod stops muting the
    /// game's own music.
    /// </summary>
    internal sealed class WaveOutPlayer : IDisposable
    {
        public delegate void RenderCallback(float[] interleaved, int frames);

        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_NULL = 0;
        private const int WHDR_DONE = 0x1;
        private const int MMSYSERR_NOERROR = 0;
        private const int StallMilliseconds = 2000;
        private const int RetryMilliseconds = 2000;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormatEx
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hWaveOut, IntPtr uDeviceID, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hWaveOut);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hWaveOut);

        private readonly int sampleRate;
        private readonly int framesPerBuffer;
        private readonly int bufferCount;
        private readonly RenderCallback render;
        private readonly int headerSize = Marshal.SizeOf(typeof(WaveHdr));
        private readonly int flagsOffset = (int)Marshal.OffsetOf(typeof(WaveHdr), "dwFlags");

        private IntPtr device;
        private IntPtr[] headers;
        private IntPtr[] data;
        private float[] floatBuffer;
        private short[] shortBuffer;
        private Thread thread;
        private volatile bool running;
        private volatile bool healthy;
        private bool writeFailed;
        private int renderErrors;

        public bool Healthy => healthy;

        public WaveOutPlayer(int sampleRate, int framesPerBuffer, int bufferCount, RenderCallback render)
        {
            this.sampleRate = sampleRate;
            this.framesPerBuffer = framesPerBuffer;
            this.bufferCount = bufferCount;
            this.render = render;
        }

        public void Start()
        {
            int bytes = framesPerBuffer * 4;
            headers = new IntPtr[bufferCount];
            data = new IntPtr[bufferCount];
            floatBuffer = new float[framesPerBuffer * 2];
            shortBuffer = new short[framesPerBuffer * 2];
            for (int i = 0; i < bufferCount; i++)
            {
                data[i] = Marshal.AllocHGlobal(bytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
            }

            int res = OpenDevice();
            if (res != MMSYSERR_NOERROR)
            {
                FreeBuffers();
                throw new InvalidOperationException("waveOutOpen failed with code " + res + " (no audio output device?)");
            }

            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "CalamityMusic Audio", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        private int OpenDevice()
        {
            var fmt = new WaveFormatEx
            {
                wFormatTag = 1, // PCM
                nChannels = 2,
                nSamplesPerSec = sampleRate,
                wBitsPerSample = 16,
                nBlockAlign = 4,
                nAvgBytesPerSec = sampleRate * 4,
                cbSize = 0,
            };
            int res = waveOutOpen(out device, new IntPtr(WAVE_MAPPER), ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
            if (res != MMSYSERR_NOERROR)
            {
                device = IntPtr.Zero;
                healthy = false;
                return res;
            }

            for (int i = 0; i < bufferCount; i++)
            {
                var hdr = new WaveHdr { lpData = data[i], dwBufferLength = framesPerBuffer * 4 };
                Marshal.StructureToPtr(hdr, headers[i], false);
                res = waveOutPrepareHeader(device, headers[i], headerSize);
                if (res != MMSYSERR_NOERROR)
                {
                    for (int j = 0; j < i; j++) waveOutUnprepareHeader(device, headers[j], headerSize);
                    waveOutClose(device);
                    device = IntPtr.Zero;
                    healthy = false;
                    return res;
                }
            }
            healthy = true;
            return MMSYSERR_NOERROR;
        }

        private void CloseDevice()
        {
            if (device == IntPtr.Zero) return;
            waveOutReset(device);
            for (int i = 0; i < bufferCount; i++) waveOutUnprepareHeader(device, headers[i], headerSize);
            waveOutClose(device);
            device = IntPtr.Zero;
        }

        private void Fill(int index)
        {
            try
            {
                render(floatBuffer, framesPerBuffer);
            }
            catch (Exception e)
            {
                Array.Clear(floatBuffer, 0, floatBuffer.Length);
                if (renderErrors++ < 5) Log.Error("Audio render failed: " + e);
            }

            for (int i = 0; i < floatBuffer.Length; i++)
            {
                float s = floatBuffer[i] * 32767f;
                if (s > 32767f) s = 32767f; else if (s < -32768f) s = -32768f;
                shortBuffer[i] = (short)s;
            }
            Marshal.Copy(shortBuffer, 0, data[index], shortBuffer.Length);
            if (waveOutWrite(device, headers[index], headerSize) != MMSYSERR_NOERROR) writeFailed = true;
        }

        private void Loop()
        {
            int next = 0;
            var sinceProgress = Stopwatch.StartNew();
            for (int i = 0; i < bufferCount; i++) Fill(i);

            while (running)
            {
                if (!healthy)
                {
                    Thread.Sleep(RetryMilliseconds);
                    if (!running) break;
                    if (OpenDevice() == MMSYSERR_NOERROR)
                    {
                        Log.Info("Audio output device reopened.");
                        writeFailed = false;
                        next = 0;
                        for (int i = 0; i < bufferCount; i++) Fill(i);
                        sinceProgress.Restart();
                    }
                    continue;
                }

                bool progressed = false;
                while (running && !writeFailed && (Marshal.ReadInt32(headers[next], flagsOffset) & WHDR_DONE) != 0)
                {
                    Fill(next);
                    next = (next + 1) % bufferCount;
                    progressed = true;
                }

                if (progressed)
                {
                    sinceProgress.Restart();
                }
                else if (writeFailed || sinceProgress.ElapsedMilliseconds > StallMilliseconds)
                {
                    Log.Warning("Audio output device stopped responding, reopening it.");
                    CloseDevice();
                    healthy = false;
                    writeFailed = false;
                    sinceProgress.Restart();
                }
                else
                {
                    Thread.Sleep(4);
                }
            }
        }

        private void FreeBuffers()
        {
            if (headers == null) return;
            for (int i = 0; i < bufferCount; i++)
            {
                if (headers[i] != IntPtr.Zero) Marshal.FreeHGlobal(headers[i]);
                if (data[i] != IntPtr.Zero) Marshal.FreeHGlobal(data[i]);
            }
            headers = null;
            data = null;
        }

        /// <summary>Stops playback. Returns false if the audio thread could not be stopped in time.</summary>
        public bool Stop()
        {
            running = false;
            if (thread != null && thread.IsAlive && !thread.Join(1000))
            {
                // Still busy (slow disk read?). Leave the device and buffers alone rather than
                // freeing memory the thread may still touch; the process is usually exiting anyway.
                return false;
            }
            CloseDevice();
            FreeBuffers();
            return true;
        }

        public void Dispose() => Stop();
    }
}
