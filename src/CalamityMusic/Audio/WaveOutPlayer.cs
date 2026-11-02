using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CalamityMusic.Audio
{
    /// <summary>
    /// Plays audio through the Windows waveOut API on its own thread. Risk of Rain 2 routes all of its
    /// audio through Wwise and has Unity's audio engine switched off, so custom music needs its own
    /// output. waveOut is available on every Windows install and works under Proton/Wine too.
    /// Buffers live in unmanaged memory so the garbage collector can never move them mid-playback.
    /// </summary>
    internal sealed class WaveOutPlayer : IDisposable
    {
        public delegate void RenderCallback(float[] interleaved, int frames);

        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_NULL = 0;
        private const int WHDR_DONE = 0x1;
        private const int MMSYSERR_NOERROR = 0;

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

        public bool IsOpen => device != IntPtr.Zero;

        public WaveOutPlayer(int sampleRate, int framesPerBuffer, int bufferCount, RenderCallback render)
        {
            this.sampleRate = sampleRate;
            this.framesPerBuffer = framesPerBuffer;
            this.bufferCount = bufferCount;
            this.render = render;
        }

        public void Start()
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
                throw new InvalidOperationException("waveOutOpen failed with code " + res + " (no audio output device?)");
            }

            int bytes = framesPerBuffer * 4;
            headers = new IntPtr[bufferCount];
            data = new IntPtr[bufferCount];
            floatBuffer = new float[framesPerBuffer * 2];
            shortBuffer = new short[framesPerBuffer * 2];
            for (int i = 0; i < bufferCount; i++)
            {
                data[i] = Marshal.AllocHGlobal(bytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
                var hdr = new WaveHdr { lpData = data[i], dwBufferLength = bytes };
                Marshal.StructureToPtr(hdr, headers[i], false);
                waveOutPrepareHeader(device, headers[i], headerSize);
            }

            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "CalamityMusic Audio", Priority = ThreadPriority.AboveNormal };
            thread.Start();
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
                Log.Error("Audio render failed: " + e);
            }

            for (int i = 0; i < floatBuffer.Length; i++)
            {
                float s = floatBuffer[i] * 32767f;
                if (s > 32767f) s = 32767f; else if (s < -32768f) s = -32768f;
                shortBuffer[i] = (short)s;
            }
            Marshal.Copy(shortBuffer, 0, data[index], shortBuffer.Length);
            Marshal.WriteInt32(headers[index], flagsOffset, Marshal.ReadInt32(headers[index], flagsOffset) & ~WHDR_DONE);
            waveOutWrite(device, headers[index], headerSize);
        }

        private void Loop()
        {
            for (int i = 0; i < bufferCount; i++) Fill(i);
            int next = 0;
            while (running)
            {
                bool wroteAny = false;
                while (running && (Marshal.ReadInt32(headers[next], flagsOffset) & WHDR_DONE) != 0)
                {
                    Fill(next);
                    next = (next + 1) % bufferCount;
                    wroteAny = true;
                }
                if (!wroteAny) Thread.Sleep(4);
            }
        }

        public void Dispose()
        {
            running = false;
            if (thread != null && thread.IsAlive) thread.Join(500);
            if (device != IntPtr.Zero)
            {
                waveOutReset(device);
                for (int i = 0; i < bufferCount; i++)
                {
                    waveOutUnprepareHeader(device, headers[i], headerSize);
                    Marshal.FreeHGlobal(headers[i]);
                    Marshal.FreeHGlobal(data[i]);
                }
                waveOutClose(device);
                device = IntPtr.Zero;
            }
        }
    }
}
