using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Doom2D.Engine
{
    /// <summary>
    /// The XNA end of the renderer: turns the 8-bit framebuffer into a texture
    /// once per frame and stretches it to the window with nearest-neighbour
    /// filtering, which is exactly what rembo's HQ=2 pixel doubling did.
    /// </summary>
    public sealed class Screen : IDisposable
    {
        readonly Texture2D texture;
        readonly Color[] pixels;

        public int Width { get { return texture.Width; } }
        public int Height { get { return texture.Height; } }

        public Screen(GraphicsDevice device, int width, int height)
        {
            texture = new Texture2D(device, width, height);
            pixels = new Color[width * height];
        }

        /// <summary>V_copytoscr: palette-map the framebuffer and upload it.</summary>
        public void Upload()
        {
            byte[] src = Vga.Buffer;
            Color[] pal = Palette.Colors;
            for (int i = 0; i < pixels.Length; ++i) pixels[i] = pal[src[i]];
            texture.SetData(pixels);
        }

        public void Draw(SpriteBatch batch, Rectangle destination)
        {
            batch.Draw(texture, destination, Color.White);
        }

        /// <summary>F1 screenshot: a 24-bit BMP of the framebuffer.</summary>
        public void SaveBmp(System.IO.Stream stream)
        {
            int w = Width, h = Height;
            int rowSize = (w * 3 + 3) & ~3;
            int dataSize = rowSize * h;

            System.IO.BinaryWriter bw = new System.IO.BinaryWriter(stream);
            bw.Write((byte)'B'); bw.Write((byte)'M');
            bw.Write(54 + dataSize);
            bw.Write(0);
            bw.Write(54);
            bw.Write(40);
            bw.Write(w);
            bw.Write(h);
            bw.Write((short)1);
            bw.Write((short)24);
            bw.Write(0);
            bw.Write(dataSize);
            bw.Write(2835); bw.Write(2835);
            bw.Write(0); bw.Write(0);

            byte[] src = Vga.Buffer;
            byte[] row = new byte[rowSize];
            for (int y = h - 1; y >= 0; --y)
            {
                int at = 0;
                for (int x = 0; x < w; ++x)
                {
                    int i = src[y * w + x];
                    row[at++] = (byte)(Palette.Std[i * 3 + 2] * 4);
                    row[at++] = (byte)(Palette.Std[i * 3 + 1] * 4);
                    row[at++] = (byte)(Palette.Std[i * 3] * 4);
                }
                bw.Write(row, 0, rowSize);
            }
            bw.Flush();
        }

        public void Dispose()
        {
            texture.Dispose();
        }
    }
}
