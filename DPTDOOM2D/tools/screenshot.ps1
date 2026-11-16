param(
    [int]$ProcessId,
    [string]$Out = "$env:TEMP\shot.png",
    [string]$WindowTitle = "DPT.DOOM 2D",
    [int]$DelayMs = 600,
    [string]$Keys = ""
)

<#
    Sends a key script to the game window, then captures its client area.

    Keys is a comma-separated list like "F2" or "Down,Down,Right". Each entry is
    pressed for ~60 ms, which is longer than one 50 ms game tick, so the game
    sees exactly one press — the same as a human tap.
#>

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class DptKey {
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetFocus(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static IntPtr FindByTitle(string title, uint pid) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr p) {
            if (!IsWindowVisible(h)) return true;
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (pid != 0 && owner != pid) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            StringBuilder sb = new StringBuilder(len + 2);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@

$vk = @{
    'F1'=0x70; 'F2'=0x71; 'F3'=0x72; 'F4'=0x73; 'F5'=0x74; 'F10'=0x79;
    'Tab'=0x09; 'Enter'=0x0D; 'Escape'=0x1B; 'Space'=0x20;
    'Left'=0x25; 'Up'=0x26; 'Right'=0x27; 'Down'=0x28;
    'PageDown'=0x22; 'Delete'=0x2E; 'Home'=0x24; 'End'=0x23;
    'Num4'=0x64; 'Num5'=0x65; 'Num6'=0x66; 'Num8'=0x68
}

$proc = Get-Process -Id $ProcessId
$h = [DptKey]::FindByTitle($WindowTitle, [uint32]$proc.Id)
if ($h -eq [IntPtr]::Zero) { Write-Error "window not found"; exit 1 }

[DptKey]::ShowWindow($h, 5) | Out-Null
[DptKey]::SetWindowPos($h, [IntPtr]::new(-1), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null
[DptKey]::SetForegroundWindow($h) | Out-Null
[DptKey]::SetFocus($h) | Out-Null
Start-Sleep -Milliseconds $DelayMs

if ($Keys) {
    foreach ($name in $Keys.Split(',')) {
        $key = $name.Trim()
        if (-not $vk.ContainsKey($key)) { Write-Warning "unknown key $key"; continue }
        $code = [byte]$vk[$key]
        [DptKey]::keybd_event($code, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 70
        [DptKey]::keybd_event($code, 0, 2, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 90
    }
    Start-Sleep -Milliseconds 250
}

$rect = New-Object DptKey+RECT
[DptKey]::GetClientRect($h, [ref]$rect) | Out-Null
$origin = New-Object DptKey+POINT
[DptKey]::ClientToScreen($h, [ref]$origin) | Out-Null

$w = $rect.R - $rect.L
$hgt = $rect.B - $rect.T

$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($origin.X, $origin.Y, 0, 0, $bmp.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

[DptKey]::SetWindowPos($h, [IntPtr]::new(-2), 0, 0, 0, 0, 0x0001 -bor 0x0002) | Out-Null

Write-Output "$Out ($w x $hgt)"
