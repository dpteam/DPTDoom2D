//
// DPT.DOOM 2D — audio output.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// Streaming audio out through winmm's waveOut.
    ///
    /// waveOut has been in Windows since 3.1 and needs no SDK, no COM and no
    /// extra DLL, which is why the port uses it instead of XAudio2 or SDL. Four
    /// small buffers are queued and refilled from a background thread as the
    /// driver completes them, so the mixer never blocks the game loop.
    /// </summary>
    internal sealed class AudioOut : IDisposable
    {
        public const int SampleRate = 22050;
        public const int Channels = 2;

        private const int BufferFrames = 1024;          // ~46 ms per buffer
        private const int BufferCount = 4;              // ~185 ms of slack
        private const int WAVE_MAPPER = -1;
        private const uint CALLBACK_EVENT = 0x00050000;
        private const int WHDR_DONE = 0x00000001;
        private const int WHDR_PREPARED = 0x00000002;
        private const int WHDR_INQUEUE = 0x00000010;

        private IntPtr waveOut;
        private IntPtr doneEvent;

        private readonly short[][] buffers = new short[BufferCount][];
        private readonly GCHandle[] pins = new GCHandle[BufferCount];
        private readonly WAVEHDR[] headers = new WAVEHDR[BufferCount];

        private Thread thread;
        private volatile bool running;
        private volatile bool disposed;

        /// <summary>Fills the destination with interleaved stereo samples.</summary>
        public Action<short[], int> Renderer;

        public bool IsOpen { get { return waveOut != IntPtr.Zero; } }

        public bool Open()
        {
            if (waveOut != IntPtr.Zero) return true;

            var format = new WAVEFORMATEX();
            format.wFormatTag = 1;                       // PCM
            format.nChannels = Channels;
            format.nSamplesPerSec = SampleRate;
            format.wBitsPerSample = 16;
            format.nBlockAlign = (ushort)(Channels * 2);
            format.nAvgBytesPerSec = SampleRate * format.nBlockAlign;
            format.cbSize = 0;

            doneEvent = CreateEvent(IntPtr.Zero, false, false, null);
            if (doneEvent == IntPtr.Zero) return false;

            int result = waveOutOpen(out waveOut, WAVE_MAPPER, ref format, doneEvent, IntPtr.Zero, CALLBACK_EVENT);
            if (result != 0)
            {
                waveOut = IntPtr.Zero;
                CloseHandle(doneEvent);
                doneEvent = IntPtr.Zero;
                return false;
            }

            for (int i = 0; i < BufferCount; ++i)
            {
                buffers[i] = new short[BufferFrames * Channels];
                pins[i] = GCHandle.Alloc(buffers[i], GCHandleType.Pinned);

                headers[i] = new WAVEHDR();
                headers[i].lpData = pins[i].AddrOfPinnedObject();
                headers[i].dwBufferLength = (uint)(buffers[i].Length * 2);
                waveOutPrepareHeader(waveOut, ref headers[i], Marshal.SizeOf(typeof(WAVEHDR)));
            }

            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "dptdoom2d-audio";
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();

            return true;
        }

        private void Loop()
        {
            while (running)
            {
                WaitForSingleObject(doneEvent, 100);

                for (int i = 0; i < BufferCount && running; ++i)
                {
                    if ((headers[i].dwFlags & WHDR_INQUEUE) != 0) continue;

                    short[] buffer = buffers[i];
                    if (Renderer != null) Renderer(buffer, BufferFrames);
                    else Array.Clear(buffer, 0, buffer.Length);

                    headers[i].dwFlags &= ~WHDR_DONE;
                    waveOutWrite(waveOut, ref headers[i], Marshal.SizeOf(typeof(WAVEHDR)));
                }
            }
        }

        /// <summary>Drops everything queued: used when a map change replaces every sound.</summary>
        public void Reset()
        {
            if (waveOut == IntPtr.Zero) return;
            waveOutReset(waveOut);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            running = false;
            if (thread != null)
            {
                thread.Join(500);
                thread = null;
            }

            if (waveOut != IntPtr.Zero)
            {
                waveOutReset(waveOut);

                for (int i = 0; i < BufferCount; ++i)
                {
                    waveOutUnprepareHeader(waveOut, ref headers[i], Marshal.SizeOf(typeof(WAVEHDR)));
                    if (pins[i].IsAllocated) pins[i].Free();
                }

                waveOutClose(waveOut);
                waveOut = IntPtr.Zero;
            }

            if (doneEvent != IntPtr.Zero)
            {
                CloseHandle(doneEvent);
                doneEvent = IntPtr.Zero;
            }
        }

        // ---- winmm / kernel32 ------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr handle, int device, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr handle);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr handle, ref WAVEHDR header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr handle, ref WAVEHDR header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr handle, ref WAVEHDR header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    }
}
