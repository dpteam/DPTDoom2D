using System;

namespace Terraria
{
    /// <summary>
    /// The donor tree's SpriteBatch calls Terraria.Program.LogWarn when a
    /// Begin/End pair does not balance (TerrariaXDK logs to Storage/Logs). Doom2D
    /// has no such log file, but the call has to resolve, so it goes to stderr.
    ///
    /// This shim exists only because the framework sources are linked from
    /// ..\TerrariaXDK\Source rather than copied: keep the donor untouched.
    /// </summary>
    public static class Program
    {
        public static void LogWarn(string source, string message)
        {
            Console.Error.WriteLine("[" + source + "] " + message);
        }
    }
}
