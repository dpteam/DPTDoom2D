//
// DPT.DOOM 2D — lockstep network session.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Shared;

namespace DPTDOOM2D.Net
{
    /// <summary>
    /// A two-player lockstep session.
    ///
    /// Both machines run the same simulation. Each tick, each side sends the input
    /// it applied; a tick is only simulated once the other side's input for it has
    /// arrived. Nothing else crosses the wire, so the bandwidth is a few dozen
    /// bytes per tick and the two games cannot drift apart unless they compute
    /// something differently — which the periodic checksum catches.
    ///
    /// The design follows DPT.DOOM's session, reduced to what Doom2D needs: two
    /// players, one shared RNG seed, no bots.
    /// </summary>
    internal sealed class NetSession : IDisposable
    {
        /// <summary>How many ticks of input to buffer before the first one runs.
        ///
        /// Two ticks is enough for a LAN and keeps the controls responsive; a
        /// higher number would hide more jitter but make every action feel late.
        /// </summary>
        public const int InputDelay = 2;

        /// <summary>A peer that has not sent anything for this long is considered gone.</summary>
        public const int TimeoutMs = 15000;

        private enum Role { None, Host, Guest }

        private Role role;
        private TcpClient client;
        private Stream stream;
        private Thread reader;

        private volatile bool running;
        private volatile bool disposed;
        private volatile string failure;

        private readonly Queue<byte[]> incoming = new Queue<byte[]>();
        private readonly object incomingLock = new object();

        // ---- session state ---------------------------------------------------
        private int localPlayer;
        private int remotePlayer;
        private uint seed;
        private int startMap;
        private bool deathmatch;

        // ---- lockstep --------------------------------------------------------
        private readonly Dictionary<int, ushort> localInput = new Dictionary<int, ushort>();
        private readonly Dictionary<int, ushort> remoteInput = new Dictionary<int, ushort>();
        private readonly Dictionary<int, uint> remoteChecksum = new Dictionary<int, uint>();

        private int currentTick;
        private int lastRemoteTick = -1;
        private int lastReceiveMs;

        private readonly Queue<string> chat = new Queue<string>();

        private int desyncTick = -1;

        // ---- accessors -------------------------------------------------------

        public bool Active { get { return role != Role.None && failure == null; } }
        public bool IsHost { get { return role == Role.Host; } }
        public string Failure { get { return failure; } }
        public uint Seed { get { return seed; } }
        public int StartMap { get { return startMap; } }
        public bool Deathmatch { get { return deathmatch; } }
        public int LocalPlayer { get { return localPlayer; } }
        public int DesyncTick { get { return desyncTick; } }

        /// <summary>Ticks the local machine is waiting for the peer's input.</summary>
        public int StalledTicks { get; private set; }

        /// <summary>Rough round-trip time, from the ping/pong pair.</summary>
        public int PingMs { get; private set; }

        // ---- host ------------------------------------------------------------

        /// <summary>Starts listening and waits for one guest, blocking with a progress callback.</summary>
        public bool StartHost(int port, string password, int map, bool dm, Action<string> progress)
        {
            role = Role.Host;
            localPlayer = 1;
            remotePlayer = 2;
            startMap = map;
            deathmatch = dm;

            // The accept loop below watches this flag, so it has to be set before
            // the listener starts rather than in BeginReading().
            running = true;

            TcpListener listener;
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
            }
            catch (SocketException e)
            {
                failure = "cannot listen on port " + port + ": " + e.Message;
                return false;
            }

            if (progress != null) progress("waiting for a player on port " + port + " ...");

            // Accept with a deadline so a forgotten host does not hang forever.
            IAsyncResult pending = listener.BeginAcceptTcpClient(null, null);
            int waited = 0;
            while (!pending.AsyncWaitHandle.WaitOne(200))
            {
                waited += 200;
                if (!running)
                {
                    listener.Stop();
                    failure = "cancelled";
                    return false;
                }
            }

            client = listener.EndAcceptTcpClient(pending);
            listener.Stop();

            client.NoDelay = true;
            return HandshakeAsHost(client.GetStream(), password, progress);
        }

        /// <summary>
        /// Hosts through an already-open stream (a relay channel).
        ///
        /// This is the NAT-friendly path: both players connect out to the master,
        /// so no port has to be forwarded. Everything after the stream is identical
        /// to a direct connection, which is what keeps a relayed session from
        /// behaving differently.
        /// </summary>
        public bool StartHostOnStream(Stream stream, string password, int map, bool dm, Action<string> progress)
        {
            role = Role.Host;
            localPlayer = 1;
            remotePlayer = 2;
            startMap = map;
            deathmatch = dm;
            running = true;

            if (progress != null) progress("relay channel open, waiting for the handshake");

            return HandshakeAsHost(stream, password, progress);
        }

        private bool HandshakeAsHost(Stream stream, string password, Action<string> progress)
        {
            this.stream = stream;
            lastReceiveMs = Environment.TickCount;

            // The guest speaks first with Hello; the host answers with Welcome,
            // which is where the seed and the rules travel.
            byte[] hello = ReadFrameBlocking(30000);
            if (hello == null)
            {
                failure = "the guest did not send a handshake";
                return false;
            }

            byte type;
            byte[] payload;
            if (!Unframe(hello, out type, out payload) || type != NetProtocol.Hello || payload.Length < 3)
            {
                failure = "malformed handshake";
                return false;
            }

            int version = NetProtocol.GetU16(payload, 0);
            if (version != NetProtocol.Version)
            {
                SendRefuse(NetProtocol.RefuseVersion);
                failure = "the guest runs protocol " + version + ", this build speaks " + NetProtocol.Version;
                return false;
            }

            string guestPassword = NetProtocol.GetText(payload, 2, Math.Min(32, payload.Length - 2));
            if (!string.IsNullOrEmpty(password) && guestPassword != password)
            {
                SendRefuse(NetProtocol.RefusePassword);
                failure = "the guest gave the wrong password";
                return false;
            }

            seed = Rng.NewSeed();

            var welcome = new byte[2 + 1 + 4 + 1 + 1 + 1 + 8];
            NetProtocol.PutU16(welcome, 0, NetProtocol.Version);
            welcome[2] = (byte)remotePlayer;
            NetProtocol.PutU32(welcome, 3, seed);
            welcome[7] = (byte)startMap;
            welcome[8] = (byte)(deathmatch ? 1 : 0);
            welcome[9] = (byte)InputDelay;
            NetProtocol.PutText(welcome, 10, 8, "DPT2D");

            Send(NetProtocol.Welcome, welcome);

            BeginReading();
            if (progress != null) progress("player connected");

            return true;
        }

        // ---- guest -----------------------------------------------------------

        /// <summary>Connects to a host, or through a relay, and completes the handshake.</summary>
        public bool StartGuest(string host, int port, string password, Stream relayStream, Action<string> progress)
        {
            role = Role.Guest;
            localPlayer = 2;
            remotePlayer = 1;
            startMap = 1;
            deathmatch = false;

            if (progress != null) progress("connecting to " + host + ":" + port + " ...");

            if (relayStream != null)
            {
                stream = relayStream;
            }
            else
            {
                client = new TcpClient();
                client.NoDelay = true;

                IAsyncResult pending = client.BeginConnect(host, port, null, null);
                if (!pending.AsyncWaitHandle.WaitOne(10000))
                {
                    try { client.Close(); } catch { }
                    failure = "no answer from " + host + ":" + port;
                    return false;
                }

                try { client.EndConnect(pending); }
                catch (SocketException e) { failure = "cannot connect: " + e.Message; return false; }

                stream = client.GetStream();
            }

            lastReceiveMs = Environment.TickCount;

            var hello = new byte[2 + 32];
            NetProtocol.PutU16(hello, 0, NetProtocol.Version);
            NetProtocol.PutText(hello, 2, 32, password ?? "");

            Send(NetProtocol.Hello, hello);

            byte[] frame = ReadFrameBlocking(15000);
            if (frame == null)
            {
                failure = "the host did not answer";
                return false;
            }

            byte type;
            byte[] payload;
            if (!Unframe(frame, out type, out payload))
            {
                failure = "malformed answer from the host";
                return false;
            }

            if (type == NetProtocol.Refuse)
            {
                byte code = payload.Length > 0 ? payload[0] : (byte)0;
                failure = NetProtocol.DescribeRefusal(code);
                return false;
            }

            if (type != NetProtocol.Welcome || payload.Length < 10)
            {
                failure = "unexpected answer from the host";
                return false;
            }

            int version = NetProtocol.GetU16(payload, 0);
            if (version != NetProtocol.Version)
            {
                failure = "the host runs protocol " + version + ", this build speaks " + NetProtocol.Version;
                return false;
            }

            localPlayer = payload[2];
            seed = NetProtocol.GetU32(payload, 3);
            startMap = payload[7];
            deathmatch = payload[8] != 0;

            BeginReading();
            if (progress != null) progress("connected");

            return true;
        }

        private void SendRefuse(byte code)
        {
            Send(NetProtocol.Refuse, new byte[] { code });
        }

        // ---- stream plumbing -------------------------------------------------

        private void BeginReading()
        {
            running = true;
            reader = new Thread(ReadLoop);
            reader.IsBackground = true;
            reader.Name = "dptdoom2d-net";
            reader.Start();
        }

        private void ReadLoop()
        {
            byte[] header = new byte[NetProtocol.HeaderSize];

            try
            {
                while (running)
                {
                    if (!ReadExactly(header, header.Length)) break;

                    byte type;
                    int length;
                    if (!NetProtocol.TryReadHeader(header, out type, out length))
                    {
                        failure = "the peer sent a frame this build cannot read";
                        break;
                    }

                    byte[] payload = new byte[length];
                    if (length > 0 && !ReadExactly(payload, length)) break;

                    lastReceiveMs = Environment.TickCount;

                    var frame = new byte[2 + length];
                    frame[0] = type;
                    frame[1] = 0; // padding so the payload starts at an even offset
                    Buffer.BlockCopy(payload, 0, frame, 2, length);

                    lock (incomingLock) incoming.Enqueue(frame);
                }
            }
            catch (Exception e)
            {
                if (running) failure = "connection lost: " + e.Message;
            }
            finally
            {
                running = false;
            }
        }

        private bool ReadExactly(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int got = stream.Read(buffer, read, count - read);
                if (got <= 0) return false;
                read += got;
            }
            return true;
        }

        private byte[] ReadFrameBlocking(int timeoutMs)
        {
            stream.ReadTimeout = timeoutMs;

            byte[] header = new byte[NetProtocol.HeaderSize];
            if (!ReadExactly(header, header.Length)) return null;

            byte type;
            int length;
            if (!NetProtocol.TryReadHeader(header, out type, out length)) return null;

            byte[] payload = new byte[length];
            if (length > 0 && !ReadExactly(payload, length)) return null;

            byte[] frame = new byte[2 + length];
            frame[0] = type;
            if (length > 0) Buffer.BlockCopy(payload, 0, frame, 2, length);
            return frame;
        }

        private static bool Unframe(byte[] frame, out byte type, out byte[] payload)
        {
            type = 0;
            payload = new byte[0];
            if (frame == null || frame.Length < 2) return false;

            type = frame[0];
            int length = frame.Length - 2;
            payload = new byte[length];
            if (length > 0) Buffer.BlockCopy(frame, 2, payload, 0, length);
            return true;
        }

        private void Send(byte type, byte[] payload)
        {
            if (stream == null) return;

            try
            {
                byte[] frame = NetProtocol.Pack(type, payload);
                lock (stream) stream.Write(frame, 0, frame.Length);
            }
            catch (Exception e)
            {
                if (running) failure = "cannot send: " + e.Message;
            }
        }

        // ---- the per-tick API ------------------------------------------------

        /// <summary>
        /// Records and sends the input the local player applied this tick.
        /// Called before the tick is simulated.
        /// </summary>
        public void SendInput(int tick, ushort bits)
        {
            localInput[tick] = bits;

            var payload = new byte[4];
            NetProtocol.PutU16(payload, 0, tick);
            NetProtocol.PutU16(payload, 2, bits);
            Send(NetProtocol.Tic, payload);
        }

        /// <summary>
        /// Sends the world checksum for a tick that has just been simulated.
        ///
        /// Exactly one checksum per tick: sending one before and one after the
        /// simulation made the peer compare its "before" against the other's
        /// "after", which reports a desync on the very first tick.
        /// </summary>
        public void SendChecksum(int tick, uint checksum)
        {
            localChecksum[tick] = checksum;

            var payload = new byte[6];
            NetProtocol.PutU16(payload, 0, tick);
            NetProtocol.PutU32(payload, 2, checksum);
            Send(NetProtocol.Tic, payload);
        }

        private readonly Dictionary<int, uint> localChecksum = new Dictionary<int, uint>();

        /// <summary>
        /// Compares checksums for ticks that are old enough for the peer's answer
        /// to have arrived. Comparing the newest tick would race the network and
        /// report a desync that is really just latency.
        /// </summary>
        public void VerifyChecksums(int newestTick, int lag)
        {
            int tick = newestTick - lag;
            if (tick < 0) return;

            uint local;
            if (!localChecksum.TryGetValue(tick, out local)) return;

            uint remote;
            if (!remoteChecksum.TryGetValue(tick, out remote)) return;

            if (remote != local && desyncTick < 0)
            {
                desyncTick = tick;
                DesyncLocal = local;
                DesyncRemote = remote;
            }
        }

        /// <summary>The checksum this machine computed for the desynced tick.</summary>
        public uint DesyncLocal { get; private set; }

        /// <summary>The checksum the peer reported for the same tick.</summary>
        public uint DesyncRemote { get; private set; }

        /// <summary>
        /// Returns true when the peer's input for <paramref name="tick"/> is here,
        /// so the tick can be simulated.
        /// </summary>
        public bool TryAdvance(int tick, out ushort remoteBits)
        {
            remoteBits = 0;

            Pump();

            if (failure != null) return false;

            if (Environment.TickCount - lastReceiveMs > TimeoutMs)
            {
                failure = "the peer stopped answering";
                return false;
            }

            ushort bits;
            if (!remoteInput.TryGetValue(tick, out bits))
            {
                StalledTicks++;
                return false;
            }

            StalledTicks = 0;
            remoteBits = bits;

            // Keep the buffers bounded: a long session must not grow forever.
            if (tick % 256 == 0) Prune(tick);

            return true;
        }

        private void Prune(int tick)
        {
            var stale = new List<int>();
            foreach (int key in localInput.Keys) if (key < tick - 512) stale.Add(key);
            foreach (int key in stale) localInput.Remove(key);

            stale.Clear();
            foreach (int key in remoteInput.Keys) if (key < tick - 512) stale.Add(key);
            foreach (int key in stale) remoteInput.Remove(key);

            stale.Clear();
            foreach (int key in remoteChecksum.Keys) if (key < tick - 512) stale.Add(key);
            foreach (int key in stale) remoteChecksum.Remove(key);
        }

        /// <summary>Handles everything the reader thread has queued.</summary>
        public void Pump()
        {
            while (true)
            {
                byte[] frame = null;
                lock (incomingLock) { if (incoming.Count > 0) frame = incoming.Dequeue(); }

                if (frame == null) return;

                byte type;
                byte[] payload;
                if (!Unframe(frame, out type, out payload)) continue;

                switch (type)
                {
                    case NetProtocol.Tic:
                        if (payload.Length == 4)
                        {
                            int tick = NetProtocol.GetU16(payload, 0);
                            remoteInput[tick] = (ushort)NetProtocol.GetU16(payload, 2);
                            if (tick > lastRemoteTick) lastRemoteTick = tick;
                        }
                        else if (payload.Length == 6)
                        {
                            int tick = NetProtocol.GetU16(payload, 0);
                            remoteChecksum[tick] = NetProtocol.GetU32(payload, 2);
                        }
                        break;

                    case NetProtocol.Chat:
                        chat.Enqueue(NetProtocol.GetText(payload, 0, payload.Length));
                        break;

                    case NetProtocol.Ping:
                        Send(NetProtocol.Pong, payload);
                        break;

                    case NetProtocol.Pong:
                        {
                            uint sent = payload.Length >= 4 ? NetProtocol.GetU32(payload, 0) : 0;
                            int now = Environment.TickCount;
                            int rtt = (int)((uint)(now - (int)sent));
                            if (rtt >= 0 && rtt < 5000) PingMs = rtt;
                            break;
                        }

                    case NetProtocol.Bye:
                        failure = payload.Length > 0
                            ? NetProtocol.DescribeRefusal(payload[0])
                            : "the peer left the game";
                        break;
                }
            }
        }

        public bool TryTakeChat(out string message)
        {
            if (chat.Count == 0) { message = null; return false; }
            message = chat.Dequeue();
            return true;
        }

        public void SendChat(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > 200) text = text.Substring(0, 200);
            Send(NetProtocol.Chat, System.Text.Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Sends a ping; call a few times a second for the latency readout.</summary>
        public void SendPing()
        {
            var payload = new byte[4];
            NetProtocol.PutU32(payload, 0, (uint)Environment.TickCount);
            Send(NetProtocol.Ping, payload);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            running = false;

            try { Send(NetProtocol.Bye, new byte[0]); } catch { }
            try { if (stream != null) stream.Close(); } catch { }
            try { if (client != null) client.Close(); } catch { }

            if (reader != null)
            {
                reader.Join(500);
                reader = null;
            }

            stream = null;
            client = null;
            role = Role.None;
        }
    }
}
