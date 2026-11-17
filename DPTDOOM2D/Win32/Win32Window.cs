//
// DPT.DOOM 2D — native Win32 layer.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DPTDOOM2D.Engine;

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
        /// <summary>How the 320x200 frame is mapped onto the window.</summary>
        public enum StretchMode
        {
            /// <summary>Integer factor, letterboxed: pixels stay square.</summary>
            PixelPerfect = 0,
            /// <summary>Fill the window, nearest neighbour: crisp but uneven pixels.</summary>
            Nearest = 1,
            /// <summary>Fill the window, bilinear: soft, for high-DPI screens.</summary>
            Smooth = 2
        }

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

        // ---- mouse ----------------------------------------------------------
        //
        // The mouse is sampled, not queued: an editor or a menu only ever cares
        // where the pointer is now and which buttons are held. Wheel notches are
        // the exception — a wheel click can be swallowed by a slow frame, so they
        // accumulate and the reader takes them once.
        private int mouseX = -1;
        private int mouseY = -1;
        private int mouseButtons;
        private int wheelTicks;
        private bool mouseInside;

        /// <summary>Client coordinates of the pointer, -1 when it never entered.</summary>
        public int MouseX { get { return mouseX; } }
        public int MouseY { get { return mouseY; } }

        /// <summary>1 = left, 2 = right, 4 = middle, as WM_*BUTTON constants are ordered.</summary>
        public int MouseButtons { get { return mouseButtons; } }

        /// <summary>True while the pointer is over the client area.</summary>
        public bool MouseInside { get { return mouseInside; } }

        /// <summary>
        /// Held modifier keys, as bit flags: 1 shift, 2 control, 4 alt.
        ///
        /// Read from the key state the window already tracks, so a shortcut needs no
        /// extra message handling and works the same in the game and the editor.
        /// </summary>
        public int Modifiers
        {
            get
            {
                int bits = 0;
                if (keyDown[0x10] || keyDown[0xA0] || keyDown[0xA1]) bits |= 1;
                if (keyDown[0x11] || keyDown[0xA2] || keyDown[0xA3]) bits |= 2;
                if (keyDown[0x12] || keyDown[0xA4] || keyDown[0xA5]) bits |= 4;
                return bits;
            }
        }

        /// <summary>Wheel notches since the last call: positive is away from the user.</summary>
        public int TakeWheel()
        {
            int ticks = wheelTicks;
            wheelTicks = 0;
            return ticks;
        }

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

        /// <summary>How the frame is scaled into the client area.</summary>
        public StretchMode Stretch = StretchMode.Nearest;

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

            EnsureBackBuffer();
            if (backDc == IntPtr.Zero) return;

            // Everything goes into the back buffer and reaches the window in one
            // BitBlt. Painting the frame and then the overlay straight onto the
            // window showed the frame alone for the moment between the two calls,
            // and a WM_PAINT arriving in that window repainted it without the
            // overlay: that is the flicker.
            int dstW, dstH, offX = 0, offY = 0;
            int mode = NativeMethods.COLORONCOLOR;

            if (Stretch == StretchMode.PixelPerfect)
            {
                // Integer factor only: pixels stay square, the remainder is
                // letterboxed. This is the "as the original looked" mode.
                int factorX, factorY;
                FrameLayout(out factorX, out factorY, out offX, out offY);

                dstW = pixelWidth * factorX;
                dstH = pixelHeight * factorY;
            }
            else
            {
                // Fill the client area. Nearest keeps the pixels crisp and
                // square-ish; smooth is the bilinear one for people who prefer
                // the softer look on a high-DPI display.
                dstW = clientWidth;
                dstH = clientHeight;
                if (Stretch == StretchMode.Smooth) mode = NativeMethods.HALFTONE;
            }

            if (dstW == pixelWidth && dstH == pixelHeight && offX == 0 && offY == 0)
            {
                NativeMethods.BitBlt(backDc, 0, 0, pixelWidth, pixelHeight, memoryDc, 0, 0, NativeMethods.SRCCOPY);
            }
            else
            {
                NativeMethods.SetStretchBltMode(backDc, mode);
                NativeMethods.StretchBlt(backDc, offX, offY, dstW, dstH, memoryDc, 0, 0, pixelWidth, pixelHeight,
                    NativeMethods.SRCCOPY);
            }

            // Letterbox bars: without this the window shows whatever the desktop
            // had there before, which looks like a rendering bug.
            if (offX > 0 || offY > 0 || dstW < clientWidth || dstH < clientHeight)
            {
                FillRect(backDc, 0, 0, clientWidth, offY, 0, 0, 0, 0);
                FillRect(backDc, 0, offY + dstH, clientWidth, clientHeight - offY - dstH, 0, 0, 0, 0);
                FillRect(backDc, 0, offY, offX, dstH, 0, 0, 0, 0);
                FillRect(backDc, offX + dstW, offY, clientWidth - offX - dstW, dstH, 0, 0, 0, 0);
            }

            if (OverlayPainter != null)
            {
                try { OverlayPainter(backDc, clientWidth, clientHeight); }
                catch (Exception e) { Log.Error("overlay: draw failed", e); }
            }

            IntPtr dc = NativeMethods.GetDC(hWnd);
            if (dc == IntPtr.Zero) return;

            try
            {
                NativeMethods.BitBlt(dc, 0, 0, clientWidth, clientHeight, backDc, 0, 0, NativeMethods.SRCCOPY);
            }
            finally
            {
                NativeMethods.ReleaseDC(hWnd, dc);
            }

            NativeMethods.ValidateRect(hWnd, IntPtr.Zero);
        }

        // ---- back buffer -----------------------------------------------------

        private IntPtr backDc;
        private IntPtr backBitmap;
        private IntPtr backPrevious;
        private int backWidth;
        private int backHeight;

        /// <summary>Client-sized offscreen surface, so a frame reaches the window whole.</summary>
        private void EnsureBackBuffer()
        {
            if (backDc != IntPtr.Zero && backWidth == clientWidth && backHeight == clientHeight) return;

            ReleaseBackBuffer();

            IntPtr screenDc = NativeMethods.GetDC(hWnd);
            if (screenDc == IntPtr.Zero) return;

            try
            {
                backDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (backDc == IntPtr.Zero) return;

                backBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, clientWidth, clientHeight);
                if (backBitmap == IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(backDc);
                    backDc = IntPtr.Zero;
                    return;
                }

                backPrevious = NativeMethods.SelectObject(backDc, backBitmap);
                backWidth = clientWidth;
                backHeight = clientHeight;
            }
            finally
            {
                NativeMethods.ReleaseDC(hWnd, screenDc);
            }
        }

        private void ReleaseBackBuffer()
        {
            if (backDc != IntPtr.Zero)
            {
                if (backPrevious != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(backDc, backPrevious);
                    backPrevious = IntPtr.Zero;
                }

                NativeMethods.DeleteDC(backDc);
                backDc = IntPtr.Zero;
            }

            if (backBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(backBitmap);
                backBitmap = IntPtr.Zero;
            }

            backWidth = 0;
            backHeight = 0;
        }

        /// <summary>Paints text with GDI: used by the overlay, never into the framebuffer.</summary>
        public void DrawText(IntPtr dc, string text, int x, int y, uint color, bool bold, int size)
        {
            IntPtr font = GetFont(bold, size);
            IntPtr previous = NativeMethods.SelectObject(dc, font);
            NativeMethods.SetBkMode(dc, 1); // TRANSPARENT
            NativeMethods.SetTextColor(dc, color);
            NativeMethods.TextOut(dc, x, y, text, text.Length);
            NativeMethods.SelectObject(dc, previous);
        }

        /// <summary>
        /// Measures text in the same font <see cref="DrawText"/> uses, so the
        /// overlay can centre a label inside its tab instead of guessing a width.
        /// </summary>
        public void MeasureText(IntPtr dc, string text, bool bold, int size, out int width, out int height)
        {
            width = 0;
            height = size;

            if (string.IsNullOrEmpty(text)) return;

            IntPtr font = GetFont(bold, size);
            IntPtr previous = NativeMethods.SelectObject(dc, font);

            var extent = new NativeMethods.SIZE();
            if (NativeMethods.GetTextExtentPoint32(dc, text, text.Length, out extent))
            {
                width = extent.cx;
                height = extent.cy;
            }

            NativeMethods.SelectObject(dc, previous);
        }

        private readonly System.Collections.Generic.Dictionary<int, IntPtr> fonts =
            new System.Collections.Generic.Dictionary<int, IntPtr>();

        private IntPtr GetFont(bool bold, int size)
        {
            int key = (size << 1) | (bold ? 1 : 0);

            IntPtr font;
            if (fonts.TryGetValue(key, out font)) return font;

            font = NativeMethods.CreateFont(size, 0, 0, 0, bold ? 700 : 400,
                0, 0, 0, 0, 0, 0, 0, 0, "Consolas");

            fonts[key] = font;
            return font;
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
                    mouseButtons = 0;
                    break;

                case NativeMethods.WM_MOUSEMOVE:
                    TrackMouse(lParam.ToInt64());
                    mouseInside = true;
                    return IntPtr.Zero;

                case NativeMethods.WM_LBUTTONDOWN:
                case NativeMethods.WM_LBUTTONUP:
                case NativeMethods.WM_RBUTTONDOWN:
                case NativeMethods.WM_RBUTTONUP:
                case NativeMethods.WM_MBUTTONDOWN:
                case NativeMethods.WM_MBUTTONUP:
                    TrackMouse(lParam.ToInt64());
                    SetButton(msg, true);
                    return IntPtr.Zero;

                case NativeMethods.WM_MOUSEWHEEL:
                    {
                        // The wheel message carries screen coordinates, not client
                        // ones, so the position is left as it is and only the notch
                        // count is taken: no editor needs a wheel position.
                        int delta = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                        wheelTicks += delta / 120;
                        return IntPtr.Zero;
                    }
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void TrackMouse(long lParam)
        {
            int x = (short)(lParam & 0xFFFF);
            int y = (short)((lParam >> 16) & 0xFFFF);

            // A fullscreen window can be told a position outside itself while a
            // button is held; clamping keeps a brush stroke from wrapping around.
            mouseX = x < 0 ? 0 : x;
            mouseY = y < 0 ? 0 : y;
        }

        private void SetButton(uint msg, bool down)
        {
            int bit = 0;

            switch (msg)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                case NativeMethods.WM_LBUTTONUP:
                    bit = 1;
                    break;

                case NativeMethods.WM_RBUTTONDOWN:
                case NativeMethods.WM_RBUTTONUP:
                    bit = 2;
                    break;

                case NativeMethods.WM_MBUTTONDOWN:
                case NativeMethods.WM_MBUTTONUP:
                    bit = 4;
                    break;
            }

            bool pressedNow = msg == NativeMethods.WM_LBUTTONDOWN
                || msg == NativeMethods.WM_RBUTTONDOWN
                || msg == NativeMethods.WM_MBUTTONDOWN;

            if (pressedNow) mouseButtons |= bit;
            else mouseButtons &= ~bit;
        }

        /// <summary>
        /// Where the 320x200 frame lands inside the client area.
        ///
        /// The blitter, the mouse mapping and any overlay all have to agree on this
        /// or a click lands on the wrong pixel. One function computes it and
        /// everything else asks: with PixelPerfect it is an integer factor with
        /// letterbox bars, otherwise the frame fills the client area.
        /// </summary>
        public void FrameLayout(out int factorX, out int factorY, out int offX, out int offY)
        {
            if (Stretch == StretchMode.PixelPerfect)
            {
                int factor = Scale;
                if (factor <= 0)
                {
                    int byWidth = clientWidth / baseWidth;
                    int byHeight = clientHeight / baseHeight;
                    factor = Math.Min(byWidth, byHeight);
                }
                if (factor < 1) factor = 1;

                factorX = factor;
                factorY = factor;
                offX = (clientWidth - baseWidth * factor) / 2;
                offY = (clientHeight - baseHeight * factor) / 2;
                return;
            }

            // Fill mode: the frame is stretched, so the two factors differ.
            factorX = clientWidth > 0 ? clientWidth : baseWidth;
            factorY = clientHeight > 0 ? clientHeight : baseHeight;
            offX = 0;
            offY = 0;
        }

        /// <summary>Logical frame pixel to client pixel, using the blitter's layout.</summary>
        public void FrameToClient(int x, int y, out int clientX, out int clientY)
        {
            int factorX, factorY, offX, offY;
            FrameLayout(out factorX, out factorY, out offX, out offY);

            if (Stretch == StretchMode.PixelPerfect)
            {
                clientX = offX + x * factorX;
                clientY = offY + y * factorY;
                return;
            }

            clientX = (int)((long)x * factorX / baseWidth);
            clientY = (int)((long)y * factorY / baseHeight);
        }

        /// <summary>Client point to framebuffer pixel, following the same layout.</summary>
        public void MouseToFrame(out int x, out int y)
        {
            int factorX, factorY, offX, offY;
            FrameLayout(out factorX, out factorY, out offX, out offY);

            if (Stretch == StretchMode.PixelPerfect)
            {
                x = factorX > 0 ? (mouseX - offX) / factorX : 0;
                y = factorY > 0 ? (mouseY - offY) / factorY : 0;
            }
            else
            {
                x = factorX > 0 ? (int)((long)mouseX * baseWidth / factorX) : 0;
                y = factorY > 0 ? (int)((long)mouseY * baseHeight / factorY) : 0;
            }

            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x >= baseWidth) x = baseWidth - 1;
            if (y >= baseHeight) y = baseHeight - 1;
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
            ReleaseBackBuffer();

            foreach (IntPtr font in fonts.Values) NativeMethods.DeleteObject(font);
            fonts.Clear();

            if (hWnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(hWnd);
                hWnd = IntPtr.Zero;
            }
        }
    }
}
