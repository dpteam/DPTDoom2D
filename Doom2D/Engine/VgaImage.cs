using System;

namespace Doom2D.Engine
{
    /// <summary>
    /// A Doom2D image, i.e. vgaimg from vga.h:
    ///
    ///     unsigned short w,h;   // size
    ///     short sx,sy;          // hot spot offset (centre of the image)
    ///     ... w*h palette indices ...
    ///
    /// The WAD stores exactly that header followed by the pixels, so a resource
    /// maps onto this type one-to-one (TITLEPIC = 8 + 320*200 = 64008 bytes).
    /// </summary>
    public sealed class VgaImage
    {
        public readonly int W;
        public readonly int H;
        public readonly int SX;
        public readonly int SY;
        public readonly byte[] Pixels;

        public VgaImage(int w, int h, int sx, int sy, byte[] pixels)
        {
            W = w;
            H = h;
            SX = sx;
            SY = sy;
            Pixels = pixels;
        }

        public static VgaImage FromRaw(byte[] raw, string name = null)
        {
            if (raw.Length < 8)
                throw new Fatal("Изображение короче заголовка");

            int w = BitConverter.ToUInt16(raw, 0);
            int h = BitConverter.ToUInt16(raw, 2);
            int sx = BitConverter.ToInt16(raw, 4);
            int sy = BitConverter.ToInt16(raw, 6);

            int need = w * h;
            if (raw.Length < 8 + need)
                throw new Fatal(string.Format("Изображение {0}x{1} обрезано: {2} < {3}",
                    w, h, raw.Length, 8 + need));

            byte[] px = new byte[need];
            Buffer.BlockCopy(raw, 8, px, 0, need);
            return new VgaImage(w, h, sx, sy, px);
        }
    }
}
