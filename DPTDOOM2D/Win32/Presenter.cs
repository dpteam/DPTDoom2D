//
// DPT.DOOM 2D — framebuffer presentation.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.IO;
using System.Runtime.InteropServices;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Win32
{
    /// <summary>
    /// Turns the 8-bit palette framebuffer into the 32-bit BGRA surface the DIB
    /// blitter expects, and saves BMP screenshots.
    ///
    /// One lookup per pixel: the palette is pre-multiplied into 32-bit colours, so
    /// presenting a 400x300 frame is a 120k-entry table walk.
    /// </summary>
    public sealed class Presenter : IDisposable
    {
        private readonly int width;
        private readonly int height;
        private readonly uint[] pixels;
        private GCHandle pin;

        public Presenter(int width, int height)
        {
            this.width = width;
            this.height = height;
            pixels = new uint[width * height];
            pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        }

        public IntPtr Pointer { get { return pin.AddrOfPinnedObject(); } }
        public int Width { get { return width; } }
        public int Height { get { return height; } }

        public void Present(Win32Window window)
        {
            byte[] source = Vga.Buffer;
            uint[] palette = Palette.Colors;
            int count = Math.Min(pixels.Length, source.Length);

            for (int i = 0; i < count; ++i) pixels[i] = palette[source[i]];

            window.Present(Pointer, width, height);
        }

        /// <summary>Writes a 24-bit BMP, the same format rembo's screenshot key produced.</summary>
        public void SaveBmp(string path)
        {
            int rowSize = (width * 3 + 3) & ~3;
            int dataSize = rowSize * height;

            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'B'); writer.Write((byte)'M');
                writer.Write(54 + dataSize); writer.Write(0); writer.Write(54);
                writer.Write(40); writer.Write(width); writer.Write(height);
                writer.Write((short)1); writer.Write((short)24);
                writer.Write(0); writer.Write(dataSize);
                writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);

                byte[] row = new byte[rowSize];
                byte[] source = Vga.Buffer;

                for (int y = height - 1; y >= 0; --y)
                {
                    int at = 0;
                    for (int x = 0; x < width; ++x)
                    {
                        int index = source[y * width + x];
                        row[at++] = (byte)Math.Min(255, Palette.Std[index * 3 + 2] * 4);
                        row[at++] = (byte)Math.Min(255, Palette.Std[index * 3 + 1] * 4);
                        row[at++] = (byte)Math.Min(255, Palette.Std[index * 3] * 4);
                    }
                    writer.Write(row, 0, rowSize);
                }
            }
        }

        public void Dispose()
        {
            if (pin.IsAllocated) pin.Free();
        }
    }
}
