//
// DPT.DOOM 2D — audio output.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Runtime.InteropServices;
using System.Threading;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Audio
{
    /// <summary>
    /// Streaming audio out through winmm's waveOut.
    ///
    /// waveOut has been in Windows since 3.1 and needs no SDK, no COM and no
    /// extra DLL, which is why the port uses it instead of XAudio2 or SDL. Four
    /// small buffers are queued and refilled from a background thread as the
    /// driver completes them, so the mixer never blocks the game loop.
    ///
    /// The WAVEHDR structures live in **unmanaged** memory. A managed array of
    /// headers is a trap: the driver keeps writing into the address it was given,
    /// and the GC is free to move that array, so completions are lost and the
    /// stream dies after a few seconds. That bug sounds exactly like "music plays
    /// sometimes and then stops".
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
        private const int WAVEHDR_SIZE = 48;

        private IntPtr waveOut;
        private IntPtr doneEvent;

        private readonly short[][] buffers = new short[BufferCount][];
        private readonly GCHandle[] pins = new GCHandle[BufferCount];
        private IntPtr headers;               // BufferCount * WAVEHDR, unmanaged

        private Thread thread;
        private volatile bool running;
        private volatile bool disposed;

        /// <summary>Fills the destination with interleaved stereo samples.</summary>
        public Action<short[], int> Renderer;

        public bool IsOpen { get { return waveOut != IntPtr.Zero; } }

        /// <summary>How many buffers the driver has returned since the last reset.</summary>
        public int CompletedBuffers;

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
            if (doneEvent == IntPtr.Zero)
            {
                Log.Warn("audio: CreateEvent failed, running without sound");
                return false;
            }

            int result = waveOutOpen(out waveOut, WAVE_MAPPER, ref format, doneEvent, IntPtr.Zero, CALLBACK_EVENT);
            if (result != 0)
            {
                // MMSYSERR_NODRIVER (6), MMSYSERR_ALLOCATED (4), ...
                Log.Warn("audio: waveOutOpen failed with code " + result + ", running without sound");
                waveOut = IntPtr.Zero;
                CloseHandle(doneEvent);
                doneEvent = IntPtr.Zero;
                return false;
            }

            for (int i = 0; i < BufferCount; ++i)
            {
                buffers[i] = new short[BufferFrames * Channels];
                pins[i] = GCHandle.Alloc(buffers[i], GCHandleType.Pinned);
            }

            headers = Marshal.AllocHGlobal(WAVEHDR_SIZE * BufferCount);

            for (int i = 0; i < BufferCount; ++i)
            {
                WriteHeader(i, pins[i].AddrOfPinnedObject(), (uint)(buffers[i].Length * 2), 0);
                int prepared = waveOutPrepareHeader(waveOut, HeaderPtr(i), WAVEHDR_SIZE);
                if (prepared != 0) Log.Warn("audio: waveOutPrepareHeader(" + i + ") = " + prepared);
            }

            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "dptdoom2d-audio";
            thread.Priority = ThreadPriority.AboveNormal;
            thread.Start();

            Log.Info("audio: waveOut opened at " + SampleRate + " Hz stereo, " + BufferCount + " buffers");
            return true;
        }

        // ---- unmanaged header access ----------------------------------------

        private IntPtr HeaderPtr(int index)
        {
            return new IntPtr(headers.ToInt64() + index * WAVEHDR_SIZE);
        }

        private void WriteHeader(int index, IntPtr data, uint length, int flags)
        {
            IntPtr at = HeaderPtr(index);
            Marshal.WriteIntPtr(at, 0, data);        // lpData
            Marshal.WriteInt32(at, 8, (int)length);  // dwBufferLength
            Marshal.WriteInt32(at, 12, 0);           // dwBytesRecorded
            Marshal.WriteIntPtr(at, 16, IntPtr.Zero);// dwUser
            Marshal.WriteInt32(at, 24, flags);       // dwFlags
            Marshal.WriteInt32(at, 28, 0);           // dwLoops
            Marshal.WriteIntPtr(at, 32, IntPtr.Zero);// lpNext
            Marshal.WriteIntPtr(at, 40, IntPtr.Zero);// reserved
        }

        private int ReadFlags(int index)
        {
            return Marshal.ReadInt32(HeaderPtr(index), 24);
        }

        private void Loop()
        {
            while (running)
            {
                // The event is auto-reset and signalled once per completed buffer.
                WaitForSingleObject(doneEvent, 100);

                for (int i = 0; i < BufferCount && running; ++i)
                {
                    int flags = ReadFlags(i);

                    if ((flags & WHDR_INQUEUE) != 0) continue;
                    if ((flags & WHDR_PREPARED) == 0) continue;

                    short[] buffer = buffers[i];
                    try
                    {
                        if (Renderer != null) Renderer(buffer, BufferFrames);
                        else Array.Clear(buffer, 0, buffer.Length);
                    }
                    catch (Exception e)
                    {
                        // A broken renderer must not kill the audio thread: silence
                        // and a log line is recoverable, a dead thread is not.
                        Array.Clear(buffer, 0, buffer.Length);
                        Log.Error("audio: renderer failed", e);
                    }

                    // Only the DONE bit is cleared. Rewriting the whole header here
                    // wiped WHDR_PREPARED too, and waveOutWrite then failed with
                    // WAVERR_UNPREPARED (34) on every buffer — the game had no
                    // sound at all while looking perfectly healthy.
                    Marshal.WriteInt32(HeaderPtr(i), 24, flags & ~WHDR_DONE);

                    int written = waveOutWrite(waveOut, HeaderPtr(i), WAVEHDR_SIZE);
                    if (written != 0) Log.Warn("audio: waveOutWrite(" + i + ") = " + written);
                }

                CompletedBuffers++;
            }
        }

        /// <summary>Drops everything queued: used when a map change replaces every sound.</summary>
        public void Reset()
        {
            if (waveOut == IntPtr.Zero) return;

            try
            {
                waveOutReset(waveOut);

                // After a reset every header is unprepared, so they all have to be
                // prepared again before they can be written to.
                for (int i = 0; i < BufferCount; ++i)
                {
                    waveOutUnprepareHeader(waveOut, HeaderPtr(i), WAVEHDR_SIZE);
                    WriteHeader(i, pins[i].AddrOfPinnedObject(), (uint)(buffers[i].Length * 2), 0);
                    waveOutPrepareHeader(waveOut, HeaderPtr(i), WAVEHDR_SIZE);
                }
            }
            catch (Exception e)
            {
                Log.Warn("audio: reset failed: " + Log.Describe(e));
            }
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
                    waveOutUnprepareHeader(waveOut, HeaderPtr(i), WAVEHDR_SIZE);

                waveOutClose(waveOut);
                waveOut = IntPtr.Zero;
            }

            if (headers != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(headers);
            }

            for (int i = 0; i < BufferCount; ++i)
                if (pins[i].IsAllocated) pins[i].Free();

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

        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr handle, int device, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr handle);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    }
}
