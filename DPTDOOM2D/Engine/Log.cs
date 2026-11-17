//
// DPT.DOOM 2D — logging.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Diagnostics;
using System.IO;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// The log: one file per run plus the console, wired through <see cref="Trace"/>.
    ///
    /// Two listeners, each writing once. The classic duplication bug comes from
    /// leaving <c>DefaultTraceListener</c> in place (it writes to the debugger) or
    /// from adding a listener without clearing the list first, so the list is
    /// cleared explicitly. No App.config: the listeners are created in code, which
    /// keeps the exe self-contained and the behaviour identical on XP.
    ///
    /// Nothing here is allowed to throw: logging must never be the reason a game
    /// fails to start.
    /// </summary>
    public static class Log
    {
        /// <summary>
        /// Where the log files go: &lt;exe&gt;/Storage/Logs.
        ///
        /// Everything the game writes for itself — logs, screenshots, the launcher
        /// page — lives under Storage, so a copy of the game folder has one
        /// directory that is safe to delete and one that is not.
        /// </summary>
        public static string Directory { get; private set; }

        /// <summary>The file for this run, or null when logging is off.</summary>
        public static string FilePath { get; private set; }

        static bool active;

        public static void Start()
        {
            if (active) return;
            active = true;

            try
            {
                Directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage", "Logs");
                System.IO.Directory.CreateDirectory(Directory);

                FilePath = Path.Combine(Directory,
                    "log_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".log");

                // Clear first: DefaultTraceListener writes to the debugger and
                // would otherwise duplicate everything in a debugger session.
                Trace.Listeners.Clear();

                Trace.Listeners.Add(new ConsoleTraceListener(false));
                Trace.Listeners.Add(new TextWriterTraceListener(FilePath));
                Trace.AutoFlush = true;
            }
            catch
            {
                // A read-only folder, a full disk, a locked file: the game still
                // has to start. Logging then only goes to the console.
                Directory = null;
                FilePath = null;
                active = false;
            }
        }

        public static void Info(string message)
        {
            Write("INFO ", message);
        }

        public static void Warn(string message)
        {
            Write("WARN ", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Error(string message, Exception exception)
        {
            Write("ERROR", message + ": " + Describe(exception));
        }

        /// <summary>One-line description of an exception, including inner ones.</summary>
        public static string Describe(Exception exception)
        {
            if (exception == null) return "(no exception)";

            string text = exception.GetType().Name + ": " + exception.Message;

            Exception inner = exception.InnerException;
            int depth = 0;
            while (inner != null && depth < 3)
            {
                text += " <- " + inner.GetType().Name + ": " + inner.Message;
                inner = inner.InnerException;
                ++depth;
            }

            return text;
        }

        private static void Write(string level, string message)
        {
            try
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + " " + message;
                Trace.WriteLine(line);
            }
            catch
            {
                // Never let logging break the caller.
            }
        }

        /// <summary>Stops the listeners and flushes the file.</summary>
        public static void Stop()
        {
            try
            {
                Trace.Flush();
                Trace.Listeners.Clear();
            }
            catch
            {
            }

            active = false;
        }
    }
}
