//
// DPT.DOOM 2D — native Win32 layer.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DPTDOOM2D.Win32
{
    /// <summary>
    /// A Win32 window that owns a DIB section and blits a 32-bit BGRA frame into
    /// it. No WinForms, no SDL, no OpenGL: user32 + gdi32 only.
    ///
    /// The frame is presented at an integer scale (1x..6x) or fitted to the
    /// window, always with nearest-neighbour stretching, because Doom2D is pixel
    /// art and a bilinear filter would blur it.
    ///
    /// Input is edge-triggered: Windows sends WM_KEYDOWN over and over while a
    /// key is held, and the game must see exactly one press. The auto-repeat flag
    /// (bit 30 of lParam) is what tells the two apart.
    /// </summary>
    public sealed class Win32Window : IDisposable
    {
        private const string WindowClassName = "DPTDOOM2DWindow";

        private readonly string title;
        private readonly int baseWidth;
        private readonly int baseHeight;

        private NativeMethods.WndProc wndProcDelegate;
        private IntPtr windowIcon;
        private IntPtr hWnd;
        private IntPtr hInstance;

        private int clientWidth;
        private int clientHeight;

        private bool quit;
        private bool disposed;
        private bool focused;
        private bool fullscreenActive;
        private NativeMethods.RECT windowedRect;

        private readonly bool[] keyDown = new bool[256];
        private readonly List<int> pressed = new List<int>();
        private readonly List<int> released = new List<int>();

        private IntPtr pixels;
        private int pixelWidth;
        private int pixelHeight;
        private bool hasFrame;

        private NativeMethods.BITMAPINFO bitmapInfo;
        private IntPtr memoryDc;
        private IntPtr surface;
        private IntPtr surfaceBits;
        private IntPtr previousObject;
        private int surfaceWidth;
        private int surfaceHeight;

        private IntPtr overlayFont;

        /// <summary>Draws on top of the presented frame, in window pixels.</summary>
        public Action<IntPtr, int, int> OverlayPainter;

        public Win32Window(string title, int width, int height, int scale, bool fullscreen)
        {
            this.title = title;
            baseWidth = width;
            baseHeight = height;
            Scale = scale;
            fullscreenActive = false;

            bitmapInfo = new NativeMethods.BITMAPINFO();
            bitmapInfo.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
            bitmapInfo.bmiHeader.biPlanes = 1;
            bitmapInfo.bmiHeader.biBitCount = 32;
            bitmapInfo.bmiHeader.biCompression = NativeMethods.BI_BITFIELDS;
            // The framebuffer is written as R,G,B,A bytes, which in a little-endian
            // DWORD is 0xAARRGGBB: red in the low byte. Getting these masks the
            // other way round swaps red and blue and paints the whole game cyan.
            bitmapInfo.redMask = 0x000000FF;
            bitmapInfo.greenMask = 0x0000FF00;
            bitmapInfo.blueMask = 0x00FF0000;

            Create(fullscreen);
        }

        /// <summary>1..6 for an integer multiple of the framebuffer, 0 to fit the window.</summary>
        public int Scale { get; set; }

        public void Create(bool fullscreen)
        {
            try { NativeMethods.SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { /* Windows XP: no DPI awareness, none needed */ }

            hInstance = NativeMethods.GetModuleHandle(null);
            wndProcDelegate = WndProc;

            var wndClass = new NativeMethods.WNDCLASSEX();
            wndClass.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX));
            wndClass.style = NativeMethods.CS_HREDRAW | NativeMethods.CS_VREDRAW | NativeMethods.CS_OWNDC;
            wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
            wndClass.hInstance = hInstance;
            wndClass.hCursor = NativeMethods.LoadCursor(IntPtr.Zero, new IntPtr(NativeMethods.IDC_ARROW));
            wndClass.lpszClassName = WindowClassName;

            windowIcon = NativeMethods.LoadIcon(hInstance, new IntPtr(NativeMethods.IDI_APPLICATION));
            wndClass.hIcon = windowIcon;
            wndClass.hIconSm = windowIcon;

            if (NativeMethods.RegisterClassEx(ref wndClass) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1410) throw new Exception("RegisterClassEx failed: " + error);
            }

            uint style = NativeMethods.WS_OVERLAPPEDWINDOW | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS;
            uint exStyle = 0;
            int width = ClientToWindowWidth(ClientWidthForScale(), style, exStyle);
            int height = ClientToWindowHeight(ClientHeightForScale(), style, exStyle);

            hWnd = NativeMethods.CreateWindowEx(exStyle, WindowClassName, title, style,
                64, 64, width, height, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            if (hWnd == IntPtr.Zero)
                throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());

            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
            NativeMethods.UpdateWindow(hWnd);
            NativeMethods.SetForegroundWindow(hWnd);
            NativeMethods.SetFocus(hWnd);

            if (fullscreen) SetFullscreen(true);
            else MeasureClient();
        }

        private int ClientWidthForScale()
        {
            return Scale <= 0 ? baseWidth * 2 : baseWidth * Scale;
        }

        private int ClientHeightForScale()
        {
            return Scale <= 0 ? baseHeight * 2 : baseHeight * Scale;
        }

        private static int ClientToWindowWidth(int client, uint style, uint exStyle)
        {
            var rect = new NativeMethods.RECT { right = client, bottom = 1 };
            NativeMethods.AdjustWindowRectEx(ref rect, style, false, exStyle);
            return rect.right - rect.left;
        }

        private static int ClientToWindowHeight(int client, uint style, uint exStyle)
        {
            var rect = new NativeMethods.RECT { right = 1, bottom = client };
            NativeMethods.AdjustWindowRectEx(ref rect, style, false, exStyle);
            return rect.bottom - rect.top;
        }

        private void MeasureClient()
        {
            NativeMethods.RECT client;
            if (NativeMethods.GetClientRect(hWnd, out client))
            {
                clientWidth = client.right - client.left;
                clientHeight = client.bottom - client.top;
            }
        }

        /// <summary>Resizes the window so the client area matches the requested scale.</summary>
        public void ApplyScale(int scale)
        {
            Scale = scale;
            if (hWnd == IntPtr.Zero || fullscreenActive) return;

            uint style = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
            uint exStyle = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            int w = ClientToWindowWidth(ClientWidthForScale(), style, exStyle);
            int h = ClientToWindowHeight(ClientHeightForScale(), style, exStyle);

            NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, w, h,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);

            MeasureClient();
        }

        public void SetFullscreen(bool value)
        {
            if (hWnd == IntPtr.Zero || value == fullscreenActive) return;

            fullscreenActive = value;
            uint exStyle = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            if (value)
            {
                NativeMethods.GetWindowRect(hWnd, out windowedRect);

                uint style = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
                style &= ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                           NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX |
                           NativeMethods.WS_SYSMENU);
                NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_STYLE, (int)style);

                NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, 0, 0,
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
                    NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN),
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER);
            }
            else
            {
                uint style = (uint)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
                style |= NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                         NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX |
                         NativeMethods.WS_SYSMENU;
                NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_STYLE, (int)style);

                int w = ClientToWindowWidth(ClientWidthForScale(), style, exStyle);
                int h = ClientToWindowHeight(ClientHeightForScale(), style, exStyle);

                NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, windowedRect.left, windowedRect.top, w, h,
                    NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOZORDER);
            }

            MeasureClient();
        }

        public bool IsFullscreen { get { return fullscreenActive; } }

        public void PumpMessages()
        {
            NativeMethods.MSG msg;
            while (NativeMethods.PeekMessage(out msg, IntPtr.Zero, 0, 0, NativeMethods.PM_REMOVE))
            {
                if (msg.message == NativeMethods.WM_QUIT) { quit = true; return; }
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }

        /// <summary>Hands the window a BGRA frame and paints it, scaled.</summary>
        public void Present(IntPtr pixelBuffer, int width, int height)
        {
            pixels = pixelBuffer;
            pixelWidth = width;
            pixelHeight = height;
            hasFrame = true;
            Blit();
        }

        private void EnsureSurface()
        {
            if (memoryDc != IntPtr.Zero && surfaceWidth == pixelWidth && surfaceHeight == pixelHeight) return;

            ReleaseSurface();

            IntPtr screenDc = NativeMethods.GetDC(hWnd);
            if (screenDc == IntPtr.Zero) return;

            try
            {
                memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero) return;

                bitmapInfo.bmiHeader.biWidth = pixelWidth;
                bitmapInfo.bmiHeader.biHeight = -pixelHeight; // top-down
                bitmapInfo.bmiHeader.biSizeImage = (uint)(pixelWidth * pixelHeight * 4);

                surfaceBits = IntPtr.Zero;
                surface = NativeMethods.CreateDIBSection(screenDc, ref bitmapInfo, NativeMethods.DIB_RGB_COLORS,
                    out surfaceBits, IntPtr.Zero, 0);

                if (surface == IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(memoryDc);
                    memoryDc = IntPtr.Zero;
                    return;
                }

                previousObject = NativeMethods.SelectObject(memoryDc, surface);
                surfaceWidth = pixelWidth;
                surfaceHeight = pixelHeight;
            }
            finally
            {
                NativeMethods.ReleaseDC(hWnd, screenDc);
            }
        }

        private void ReleaseSurface()
        {
            if (memoryDc != IntPtr.Zero)
            {
                if (previousObject != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memoryDc, previousObject);
                    previousObject = IntPtr.Zero;
                }
                NativeMethods.DeleteDC(memoryDc);
                memoryDc = IntPtr.Zero;
            }

            if (surface != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(surface);
                surface = IntPtr.Zero;
            }

            surfaceBits = IntPtr.Zero;
            surfaceWidth = 0;
            surfaceHeight = 0;
        }

        private void Blit()
        {
            if (!hasFrame || hWnd == IntPtr.Zero || clientWidth <= 0 || clientHeight <= 0) return;

            EnsureSurface();
            if (memoryDc == IntPtr.Zero || surfaceBits == IntPtr.Zero) return;

            NativeMethods.CopyMemory(surfaceBits, pixels, (IntPtr)(pixelWidth * pixelHeight * 4));

            IntPtr dc = NativeMethods.GetDC(hWnd);
            if (dc == IntPtr.Zero) return;

            try
            {
                int dstW = clientWidth, dstH = clientHeight;
                int offX = 0, offY = 0;

                // Integer scaling keeps pixels square; letterbox the remainder
                // instead of stretching by a fraction.
                if (Scale > 0)
                {
                    dstW = pixelWidth * Scale;
                    dstH = pixelHeight * Scale;
                    offX = (clientWidth - dstW) / 2;
                    offY = (clientHeight - dstH) / 2;
                }

                if (dstW == pixelWidth && dstH == pixelHeight && offX == 0 && offY == 0)
                {
                    NativeMethods.BitBlt(dc, 0, 0, pixelWidth, pixelHeight, memoryDc, 0, 0, NativeMethods.SRCCOPY);
                }
                else
                {
                    NativeMethods.SetStretchBltMode(dc, NativeMethods.COLORONCOLOR);
                    NativeMethods.StretchBlt(dc, offX, offY, dstW, dstH, memoryDc, 0, 0, pixelWidth, pixelHeight,
                        NativeMethods.SRCCOPY);
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(hWnd, dc);
            }

            NativeMethods.ValidateRect(hWnd, IntPtr.Zero);

            if (OverlayPainter != null)
            {
                IntPtr overlayDc = NativeMethods.GetDC(hWnd);
                if (overlayDc != IntPtr.Zero)
                {
                    try { OverlayPainter(overlayDc, clientWidth, clientHeight); }
                    finally { NativeMethods.ReleaseDC(hWnd, overlayDc); }
                }
            }
        }

        /// <summary>Paints text with GDI: used by the overlay, never into the framebuffer.</summary>
        public void DrawText(IntPtr dc, string text, int x, int y, uint color, bool bold, int size)
        {
            if (overlayFont == IntPtr.Zero)
            {
                overlayFont = NativeMethods.CreateFont(size, 0, 0, 0, bold ? 700 : 400,
                    0, 0, 0, 0, 0, 0, 0, 0, "Consolas");
            }

            IntPtr previous = NativeMethods.SelectObject(dc, overlayFont);
            NativeMethods.SetBkMode(dc, 1); // TRANSPARENT
            NativeMethods.SetTextColor(dc, color);
            NativeMethods.TextOut(dc, x, y, text, text.Length);
            NativeMethods.SelectObject(dc, previous);
        }

        public void FillRect(IntPtr dc, int x, int y, int w, int h, int alpha, int r, int g, int b)
        {
            // GDI has no alpha blending; the overlay panels use a solid colour
            // chosen to read well over the game, which is why alpha is unused here.
            IntPtr brush = NativeMethods.CreateSolidBrush(NativeMethods.Rgb(r, g, b));
            var rect = new NativeMethods.RECT { left = x, top = y, right = x + w, bottom = y + h };
            NativeMethods.FillRect(dc, ref rect, brush);
            NativeMethods.DeleteObject(brush);
        }

        public bool IsKeyDown(int vk)
        {
            return vk >= 0 && vk < 256 && keyDown[vk];
        }

        /// <summary>Keys that went down since the last call (auto-repeat filtered out).</summary>
        public int[] TakePressed()
        {
            if (pressed.Count == 0) return EmptyKeys;
            int[] result = pressed.ToArray();
            pressed.Clear();
            return result;
        }

        public int[] TakeReleased()
        {
            if (released.Count == 0) return EmptyKeys;
            int[] result = released.ToArray();
            released.Clear();
            return result;
        }

        private static readonly int[] EmptyKeys = new int[0];

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case NativeMethods.WM_CLOSE:
                    NativeMethods.DestroyWindow(hWnd);
                    return IntPtr.Zero;

                case NativeMethods.WM_DESTROY:
                    NativeMethods.PostQuitMessage(0);
                    return IntPtr.Zero;

                case NativeMethods.WM_PAINT:
                    Blit();
                    NativeMethods.ValidateRect(hWnd, IntPtr.Zero);
                    return IntPtr.Zero;

                case NativeMethods.WM_ERASEBKGND:
                    return new IntPtr(1);

                case NativeMethods.WM_SIZE:
                    clientWidth = (int)(lParam.ToInt64() & 0xFFFF);
                    clientHeight = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    return IntPtr.Zero;

                case NativeMethods.WM_SYSCOMMAND:
                    {
                        long command = wParam.ToInt64() & 0xFFF0;
                        // Alt-Tab / menu activation and the screen saver would both
                        // disturb a game window; swallow them.
                        if (command == NativeMethods.SC_KEYMENU) return IntPtr.Zero;
                        if (command == NativeMethods.SC_SCREENSAVE) return IntPtr.Zero;
                        if (command == NativeMethods.SC_MONITORPOWER) return IntPtr.Zero;
                        break;
                    }

                case NativeMethods.WM_SYSKEYDOWN:
                case NativeMethods.WM_KEYDOWN:
                    OnKeyDown(wParam.ToInt32(), lParam.ToInt64());
                    return IntPtr.Zero;

                case NativeMethods.WM_SYSKEYUP:
                case NativeMethods.WM_KEYUP:
                    OnKeyUp(wParam.ToInt32());
                    return IntPtr.Zero;

                case NativeMethods.WM_SETFOCUS:
                    focused = true;
                    break;

                case NativeMethods.WM_KILLFOCUS:
                    focused = false;
                    ReleaseAllKeys();
                    break;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void OnKeyDown(int vk, long lParam)
        {
            bool wasDown = keyDown[vk];

            // Bit 30 is set on every repeat after the first press. Marking the
            // key down again is harmless, but queueing it a second time is not:
            // that is what made a single tap of "down" scroll the whole menu.
            bool repeat = (lParam & 0x40000000L) != 0;

            keyDown[vk] = true;

            if (!wasDown && !repeat) pressed.Add(vk);
        }

        private void OnKeyUp(int vk)
        {
            if (!keyDown[vk]) return;
            keyDown[vk] = false;
            released.Add(vk);
        }

        private void ReleaseAllKeys()
        {
            for (int i = 0; i < keyDown.Length; ++i) keyDown[i] = false;
            pressed.Clear();
            released.Clear();
        }

        public bool HasFocus { get { return focused; } }
        public bool Closed { get { return quit; } }
        public IntPtr Handle { get { return hWnd; } }
        public int ClientWidth { get { return clientWidth; } }
        public int ClientHeight { get { return clientHeight; } }

        public void SetTitle(string value)
        {
            if (hWnd != IntPtr.Zero) NativeMethods.SetWindowText(hWnd, value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            ReleaseSurface();

            if (overlayFont != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(overlayFont);
                overlayFont = IntPtr.Zero;
            }

            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(hWnd);
                hWnd = IntPtr.Zero;
            }
        }
    }
}
