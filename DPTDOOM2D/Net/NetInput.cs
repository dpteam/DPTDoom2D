//
// DPT.DOOM 2D — network glue for the game loop.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Net
{
    /// <summary>
    /// Connects the game loop to a <see cref="NetSession"/>.
    ///
    /// The loop asks three things each tick: what did the peer press, is it time to
    /// run, and does the world still match. Everything network-specific lives here
    /// so Game.Act() only has to consult <see cref="HasRemoteInput"/> and
    /// <see cref="RemoteBits"/>.
    /// </summary>
    internal static class NetInput
    {
        public static NetSession Session;

        /// <summary>True once the peer's input for the current tick has arrived.</summary>
        public static bool HasRemoteInput;

        /// <summary>The peer's input bits for the current tick.</summary>
        public static ushort RemoteBits;

        /// <summary>Which player this machine does not control.</summary>
        public static int RemotePlayer = 2;

        /// <summary>Which player this machine controls.</summary>
        public static int LocalPlayer = 1;

        /// <summary>Set when the two simulations disagree.</summary>
        public static bool Desync;

        /// <summary>How many ticks the loop has been waiting on the peer.</summary>
        public static int Waiting;

        public static bool Active { get { return Session != null && Session.Active; } }

        /// <summary>
        /// Starts the session described on the command line. Returns false when the
        /// game should not start (refused, no answer, bad arguments).
        /// </summary>
        public static bool Start(Config.NetSettings settings)
        {
            if (settings == null || !settings.Enabled) return true;

            Session = new NetSession();

            bool ok;

            if (settings.Host)
            {
                // Relay hosting goes through the master: both players connect out,
                // so no port has to be forwarded. A direct host listens locally.
                if (settings.Relay && !string.IsNullOrEmpty(settings.Master))
                {
                    if (!RegisterOnMaster(settings))
                    {
                        Console.WriteLine("net: could not register on the master; hosting directly");
                        ok = Session.StartHost(settings.Port, settings.Password, settings.Map, settings.Deathmatch,
                            line => Console.WriteLine("net: " + line));
                    }
                    else
                    {
                        ok = HostThroughRelay(settings);
                    }
                }
                else
                {
                    ok = Session.StartHost(settings.Port, settings.Password, settings.Map, settings.Deathmatch,
                        line => Console.WriteLine("net: " + line));
                }
            }
            else
            {
                System.IO.Stream relay = null;

                if (!string.IsNullOrEmpty(settings.RelaySession) && !string.IsNullOrEmpty(settings.Master))
                {
                    // Direct first, relay second: a LAN game stays direct and only
                    // an unreachable address costs the extra round trip.
                    Console.WriteLine("net: opening a relay channel through " + settings.Master);
                    try
                    {
                        var link = Shared.MasterClient.RelayJoin(
                            SplitHost(settings.Master), SplitPort(settings.Master),
                            Shared.MasterProtocol.IdFromHex(settings.RelaySession), 8000);
                        relay = link.Stream;
                        Console.WriteLine("net: relay channel open");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("net: relay failed (" + e.Message + "), trying a direct connection");
                    }
                }

                ok = Session.StartGuest(settings.Address, settings.Port, settings.Password, relay,
                    line => Console.WriteLine("net: " + line));
            }

            if (!ok)
            {
                Console.WriteLine("net: " + Session.Failure);
                Session.Dispose();
                Session = null;
                return false;
            }

            LocalPlayer = Session.LocalPlayer;
            RemotePlayer = Session.LocalPlayer == 1 ? 2 : 1;

            // The host picked the seed; both machines adopt it, which is what makes
            // the two simulations produce the same monsters in the same places.
            Rng.Seed(Session.Seed);

            Console.WriteLine("net: connected as player " + LocalPlayer + ", seed " + Session.Seed
                + ", map " + Session.StartMap + (Session.Deathmatch ? ", deathmatch" : ", coop"));

            if (settings.Host && Lease != null)
            {
                LeaseThread = new System.Threading.Thread(LeaseLoop);
                LeaseThread.IsBackground = true;
                LeaseThread.Name = "dptdoom2d-lease";
                LeaseThread.Start();
            }

            return true;
        }

        private static Shared.MasterLease Lease;
        private static Shared.MasterSessionInfo LeaseInfo;
        private static string LeaseHost;
        private static int LeasePort;
        private static System.Threading.Thread LeaseThread;

        /// <summary>
        /// Publishes the session on the master and starts the relay host side.
        ///
        /// Registration is what lets a guest behind NAT join: both ends connect out
        /// to the master, so no port has to be forwarded and no UPnP is needed.
        /// </summary>
        private static bool RegisterOnMaster(Config.NetSettings settings)
        {
            string host = SplitHost(settings.Master);
            int port = SplitPort(settings.Master);

            var info = new Shared.MasterSessionInfo();
            info.Name = "DPT.DOOM 2D " + (settings.Deathmatch ? "DM" : "COOP");
            info.MapText = "MAP" + settings.Map.ToString("00");
            info.Port = settings.Port;
            info.Players = 2;
            info.GameProtocol = Shared.MasterProtocol.GameProtocol;

            try
            {
                Lease = Shared.MasterClient.Register(host, port, info, 5000);
                LeaseInfo = info;
                LeaseHost = host;
                LeasePort = port;

                string sessionId = Shared.MasterProtocol.IdToHex(Lease.Auth);
                Console.WriteLine("net: registered on the master, session id " + sessionId);
                Console.WriteLine("net: share this id to be joined through the relay");

                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("net: master registration failed: " + e.Message);
                Lease = null;
                return false;
            }
        }

        /// <summary>Opens the relay host side and waits there for the guest.</summary>
        private static bool HostThroughRelay(Config.NetSettings settings)
        {
            try
            {
                Console.WriteLine("net: opening the relay host channel ...");
                var link = Shared.MasterClient.RelayHost(LeaseHost, LeasePort, Lease.Auth, 0);

                return Session.StartHostOnStream(link.Stream, settings.Password, settings.Map, settings.Deathmatch,
                    line => Console.WriteLine("net: " + line));
            }
            catch (Exception e)
            {
                Console.WriteLine("net: relay hosting failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Keeps the lease alive while the game runs.</summary>
        private static void LeaseLoop()        {
            int interval = Math.Max(5, Lease.HeartbeatSeconds) * 1000;

            while (Session != null && Lease != null)
            {
                System.Threading.Thread.Sleep(interval);
                if (Session == null || Lease == null) break;

                try
                {
                    int remaining = Shared.MasterClient.Heartbeat(LeaseHost, LeasePort, Lease.Auth, 5000);
                    if (remaining <= 0)
                    {
                        Console.WriteLine("net: the master dropped the session lease");
                        break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("net: heartbeat failed: " + e.Message);
                }
            }
        }

        /// <summary>
        /// Blocks until the peer's input for <paramref name="tick"/> is here.
        /// Returns false when the session died and the game must stop.
        /// </summary>
        public static bool WaitForTick(int tick)
        {
            if (Session == null) return true;

            ushort bits;

            while (!Session.TryAdvance(tick, out bits))
            {
                if (!Session.Active)
                {
                    Console.WriteLine("net: session ended: " + (Session.Failure ?? "unknown reason"));
                    return false;
                }

                Waiting++;
                System.Threading.Thread.Sleep(1);
            }

            Waiting = 0;
            RemoteBits = bits;
            HasRemoteInput = true;
            return true;
        }

        /// <summary>Sends this tick's local input, and afterwards the world checksum.</summary>
        public static void PublishInput(int tick, Game.PlayerData local)
        {
            if (Session == null) return;
            Session.SendInput(tick, Kbd.PackLocal(local));
        }

        /// <summary>Publishes the checksum for a tick that has just been simulated.</summary>
        public static void PublishChecksum(int tick, uint checksum)
        {
            if (Session == null) return;

            Session.SendChecksum(tick, checksum);

            // Compare a few ticks behind the newest one: the peer's answer for the
            // current tick cannot have arrived yet, and comparing it would report a
            // desync that is really just latency.
            Session.VerifyChecksums(tick, 4);

            if (Session.DesyncTick >= 0 && !Desync)
            {
                Desync = true;
                Console.WriteLine("net: DESYNC at tick " + Session.DesyncTick
                    + " (local " + Session.DesyncLocal.ToString("X8")
                    + ", remote " + Session.DesyncRemote.ToString("X8") + ")");
            }
        }

        public static void Stop()
        {
            if (Session == null) return;
            Session.Dispose();
            Session = null;
            HasRemoteInput = false;
        }

        public static string Status()
        {
            if (Session == null) return "offline";
            if (Desync) return "DESYNC";
            return "player " + LocalPlayer + ", ping " + Session.PingMs + " ms";
        }

        private static string SplitHost(string address)
        {
            int colon = address.IndexOf(':');
            return colon < 0 ? address : address.Substring(0, colon);
        }

        private static int SplitPort(string address)
        {
            int colon = address.IndexOf(':');
            if (colon < 0) return Shared.MasterProtocol.DefaultPort;

            int port;
            return int.TryParse(address.Substring(colon + 1), out port) ? port : Shared.MasterProtocol.DefaultPort;
        }
    }
}
