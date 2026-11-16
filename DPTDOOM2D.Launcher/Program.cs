//
// DPT.DOOM 2D — launcher entry point.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Windows.Forms;

namespace DPTDOOM2D.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }
}
