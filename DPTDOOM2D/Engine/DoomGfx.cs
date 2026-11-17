//
// DPT.DOOM 2D — Doom pictures: patches, textures, and the scaling that makes them
// usable in a game whose cells are 16 pixels.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM2D.Engine
{
    /// <summary>One decoded Doom picture: palette indices, zero meaning transparent.</summary>
    public sealed class DoomImage
    {
        public int W;
        public int H;

        /// <summary>Left and top offsets, from the patch header.</summary>
        public int Left;
        public int Top;

        public byte[] Pixels;

        public DoomImage(int w, int h, int left, int top)
        {
            W = w;
            H = h;
            Left = left;
            Top = top;
            Pixels = new byte[w * h];
        }
    }

    /// <summary>
    /// Turns Doom artwork into something Doom2D can draw.
    ///
    /// Two conversions matter, and both have a right answer that is easy to get
    /// wrong:
    ///
    ///  * **Patches are columns, not rows.** A Doom picture is stored as a list of
    ///    vertical posts with gaps between them, so reading it as a flat array of
    ///    pixels produces a picture that looks like static. The gaps are the
    ///    transparency, and index 0 is transparent as well.
    ///
    ///  * **Doom sprites are twice Doom2D's size.** Doom2D's own imp is 18x25 where
    ///    Doom's is 41x57 — the game is a 2D platformer with 16-pixel cells, and the
    ///    art was redrawn smaller. Copying Doom's art at full size puts a monster
    ///    three cells wide in a game where a corridor is two cells wide, so the
    ///    converter scales to the size the game's own sprite has. Nearest-neighbour
    ///    would turn an imp into a checkerboard; the box filter here averages the
    ///    source pixels and then finds the closest palette entry, which is what
    ///    downscaling a palette picture actually needs.
    /// </summary>
    public static class DoomGfx
    {
        /// <summary>
        /// A map from Doom's palette to the loaded Doom2D palette.
        ///
        /// The two palettes are the same ramp at different depths — Doom stores
        /// 8-bit values, Doom2D 6-bit — so index N in one is nearly index N in the
        /// other, but "nearly" shows: Doom's white is 255 and Doom2D's is 63, and a
        /// straight copy of the index leaves the picture a shade off. Mapping through
        /// RGB once, here, makes the colours exact.
        /// </summary>
        public static byte[] BuildPaletteMap(byte[] doomPalette)
        {
            var map = new byte[256];

            for (int i = 0; i < 256; ++i)
            {
                int r = doomPalette[i * 3];
                int g = doomPalette[i * 3 + 1];
                int b = doomPalette[i * 3 + 2];

                map[i] = NearestIndex(r, g, b);
            }

            return map;
        }

        /// <summary>The Doom2D palette index closest to an 8-bit colour.</summary>
        public static byte NearestIndex(int r, int g, int b)
        {
            // Doom2D's palette is 6-bit; compare in the same units.
            int qr = r >> 2, qg = g >> 2, qb = b >> 2;

            int best = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < 256; ++i)
            {
                int dr = Palette.Main[i * 3] - qr;
                int dg = Palette.Main[i * 3 + 1] - qg;
                int db = Palette.Main[i * 3 + 2] - qb;

                int distance = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = i;
            }

            return (byte)best;
        }

        // ---- patches -------------------------------------------------------

        /// <summary>
        /// Decodes a Doom patch: width, height, offsets, then one column list per
        /// column of pixels.
        ///
        /// Each column is a series of posts — topdelta, length, padding, pixels,
        /// padding — ended by 0xFF. The padding byte exists because the format comes
        /// from a 1993 renderer that wanted word alignment, and it is still there.
        /// </summary>
        public static DoomImage ReadPatch(ArraySegment<byte> lump)
        {
            byte[] raw = lump.Array;
            int at = lump.Offset;

            if (lump.Count < 8) return null;

            int w = BitConverter.ToUInt16(raw, at);
            int h = BitConverter.ToUInt16(raw, at + 2);
            int left = BitConverter.ToInt16(raw, at + 4);
            int top = BitConverter.ToInt16(raw, at + 6);

            if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return null;
            if (8 + w * 4 > lump.Count) return null;

            var image = new DoomImage(w, h, left, top);

            for (int x = 0; x < w; ++x)
            {
                int column = BitConverter.ToInt32(raw, at + 8 + x * 4);
                if (column < 0 || column >= lump.Count) continue;

                int p = at + column;

                while (p + 1 < at + lump.Count)
                {
                    int delta = raw[p];
                    if (delta == 0xFF) break;

                    int length = raw[p + 1];
                    p += 3;                       // topdelta, length, padding

                    if (p + length > at + lump.Count) break;

                    for (int i = 0; i < length; ++i)
                    {
                        int y = delta + i;
                        if (y >= 0 && y < h) image.Pixels[y * w + x] = raw[p + i];
                    }

                    p += length + 1;              // pixels, padding
                }
            }

            return image;
        }

        // ---- textures ------------------------------------------------------

        /// <summary>One texture definition: a size and the patches that fill it.</summary>
        public sealed class TextureDef
        {
            public string Name;
            public int W;
            public int H;
            public readonly List<int> PatchX = new List<int>();
            public readonly List<int> PatchY = new List<int>();
            public readonly List<int> PatchIndex = new List<int>();
        }

        /// <summary>Reads TEXTURE1 (and TEXTURE2, if present) plus PNAMES.</summary>
        public static Dictionary<string, TextureDef> ReadTextures(DoomWad wad)
        {
            var result = new Dictionary<string, TextureDef>(StringComparer.OrdinalIgnoreCase);

            List<string> patchNames = ReadPatchNames(wad);
            if (patchNames == null) return result;

            ReadTextureLump(wad, "TEXTURE1", patchNames, result);
            ReadTextureLump(wad, "TEXTURE2", patchNames, result);

            return result;
        }

        private static List<string> ReadPatchNames(DoomWad wad)
        {
            int index = wad.Find("PNAMES");
            if (index < 0) return null;

            byte[] raw = wad.Read(index);
            if (raw == null || raw.Length < 4) return null;

            int count = BitConverter.ToInt32(raw, 0);
            if (count < 0 || 4 + count * 8 > raw.Length) return null;

            var names = new List<string>(count);

            for (int i = 0; i < count; ++i)
                names.Add(System.Text.Encoding.ASCII.GetString(raw, 4 + i * 8, 8).TrimEnd('\0'));

            return names;
        }

        private static void ReadTextureLump(DoomWad wad, string lump,
            List<string> patchNames, Dictionary<string, TextureDef> result)
        {
            int index = wad.Find(lump);
            if (index < 0) return;

            byte[] raw = wad.Read(index);
            if (raw == null || raw.Length < 4) return;

            int count = BitConverter.ToInt32(raw, 0);
            if (count < 0 || 4 + count * 4 > raw.Length) return;

            for (int i = 0; i < count; ++i)
            {
                int at = BitConverter.ToInt32(raw, 4 + i * 4);
                if (at < 0 || at + 22 > raw.Length) continue;

                var texture = new TextureDef();
                texture.Name = System.Text.Encoding.ASCII.GetString(raw, at, 8).TrimEnd('\0');
                texture.W = BitConverter.ToUInt16(raw, at + 12);
                texture.H = BitConverter.ToUInt16(raw, at + 14);

                int patches = BitConverter.ToUInt16(raw, at + 20);
                if (at + 22 + patches * 10 > raw.Length) continue;

                for (int p = 0; p < patches; ++p)
                {
                    int entry = at + 22 + p * 10;

                    texture.PatchX.Add(BitConverter.ToInt16(raw, entry));
                    texture.PatchY.Add(BitConverter.ToInt16(raw, entry + 2));
                    texture.PatchIndex.Add(BitConverter.ToUInt16(raw, entry + 4));
                }

                result[texture.Name] = texture;
            }
        }

        /// <summary>
        /// Composes a texture out of its patches, the way Doom's renderer does.
        ///
        /// Patches may hang off the edge of the texture; the parts outside are
        /// dropped, which is what makes a door texture assembled from two halves work.
        /// </summary>
        public static DoomImage ComposeTexture(DoomWad wad, TextureDef texture,
            List<string> patchNames, byte[] paletteMap)
        {
            if (texture == null || texture.W <= 0 || texture.H <= 0) return null;

            var image = new DoomImage(texture.W, texture.H, 0, 0);

            for (int i = 0; i < texture.PatchIndex.Count; ++i)
            {
                int patch = texture.PatchIndex[i];
                if (patch < 0 || patch >= patchNames.Count) continue;

                int lump = wad.Find(patchNames[patch]);
                if (lump < 0) continue;

                DoomImage part = ReadPatch(wad.Segment(lump));
                if (part == null) continue;

                int ox = texture.PatchX[i];
                int oy = texture.PatchY[i];

                for (int y = 0; y < part.H; ++y)
                {
                    int dy = oy + y;
                    if (dy < 0 || dy >= texture.H) continue;

                    for (int x = 0; x < part.W; ++x)
                    {
                        int dx = ox + x;
                        if (dx < 0 || dx >= texture.W) continue;

                        byte pixel = part.Pixels[y * part.W + x];
                        if (pixel == 0) continue;

                        image.Pixels[dy * texture.W + dx] = paletteMap[pixel];
                    }
                }
            }

            return image;
        }

        // ---- scaling -------------------------------------------------------

        /// <summary>
        /// Resizes a Doom picture to the size Doom2D expects, averaging source
        /// pixels and mapping the result back into the palette.
        ///
        /// Alpha is carried separately: a sprite's transparent margin has no colour,
        /// and averaging it in would put a dark halo around every monster. A target
        /// pixel is transparent unless at least half of its source area is solid.
        /// </summary>
        public static DoomImage Resize(DoomImage source, int targetW, int targetH, byte[] paletteMap)
        {
            if (source == null || targetW <= 0 || targetH <= 0) return null;

            var result = new DoomImage(targetW, targetH, 0, 0);

            // The offsets move with the picture: a sprite whose hot spot is its feet
            // has to keep it there, or every monster floats.
            if (source.W > 0 && source.H > 0)
            {
                result.Left = source.Left * targetW / source.W;
                result.Top = source.Top * targetH / source.H;
            }

            for (int ty = 0; ty < targetH; ++ty)
            {
                int y0 = ty * source.H / targetH;
                int y1 = (ty + 1) * source.H / targetH;
                if (y1 <= y0) y1 = y0 + 1;
                if (y1 > source.H) y1 = source.H;

                for (int tx = 0; tx < targetW; ++tx)
                {
                    int x0 = tx * source.W / targetW;
                    int x1 = (tx + 1) * source.W / targetW;
                    if (x1 <= x0) x1 = x0 + 1;
                    if (x1 > source.W) x1 = source.W;

                    int r = 0, g = 0, b = 0, solid = 0, total = 0;

                    for (int sy = y0; sy < y1; ++sy)
                    {
                        int row = sy * source.W;

                        for (int sx = x0; sx < x1; ++sx)
                        {
                            ++total;

                            byte pixel = source.Pixels[row + sx];
                            if (pixel == 0) continue;

                            ++solid;

                            r += Palette.Main[pixel * 3];
                            g += Palette.Main[pixel * 3 + 1];
                            b += Palette.Main[pixel * 3 + 2];
                        }
                    }

                    if (total == 0 || solid * 2 < total) continue;

                    // Palette.Main is 6-bit, so the average is too; NearestIndex
                    // expects 8-bit, hence the shift back up.
                    byte index = NearestIndex((r / solid) << 2, (g / solid) << 2, (b / solid) << 2);

                    result.Pixels[ty * targetW + tx] = index;
                }
            }

            return result;
        }

        /// <summary>
        /// Resizes by sampling, not averaging.
        ///
        /// For a wall this is the better choice: Doom2D's wall tiles are 8x8 and 16x16
        /// crops of larger patterns, and averaging a 64x64 flat down to 8x8 leaves a
        /// flat colour where a mapper expects to see the pattern. Sampling keeps the
        /// detail and the texture reads as a texture.
        /// </summary>
        public static DoomImage ResizeNearest(DoomImage source, int targetW, int targetH)
        {
            if (source == null || targetW <= 0 || targetH <= 0) return null;

            var result = new DoomImage(targetW, targetH, 0, 0);

            result.Left = source.W > 0 ? source.Left * targetW / source.W : 0;
            result.Top = source.H > 0 ? source.Top * targetH / source.H : 0;

            for (int ty = 0; ty < targetH; ++ty)
            {
                int sy = ty * source.H / targetH;
                if (sy >= source.H) sy = source.H - 1;

                for (int tx = 0; tx < targetW; ++tx)
                {
                    int sx = tx * source.W / targetW;
                    if (sx >= source.W) sx = source.W - 1;

                    result.Pixels[ty * targetW + tx] = source.Pixels[sy * source.W + sx];
                }
            }

            return result;
        }

        /// <summary>
        /// Copies a picture without resizing, mapping its colours.
        ///
        /// Used for flats and for the font, where Doom's size is already the size
        /// Doom2D wants.
        /// </summary>
        public static DoomImage MapColours(DoomImage source, byte[] paletteMap)
        {
            if (source == null) return null;

            var result = new DoomImage(source.W, source.H, source.Left, source.Top);

            for (int i = 0; i < source.Pixels.Length; ++i)
            {
                byte pixel = source.Pixels[i];
                result.Pixels[i] = pixel == 0 ? (byte)0 : paletteMap[pixel];
            }

            return result;
        }

        /// <summary>
        /// A square flat as an opaque image.
        ///
        /// Flats have no transparency: a floor tile is solid edge to edge. Index 0 in
        /// a flat is a colour like any other, which is why this does not clear it.
        /// </summary>
        public static DoomImage FlatToImage(byte[] flat, int size, byte[] paletteMap)
        {
            var image = new DoomImage(size, size, 0, 0);

            for (int i = 0; i < size * size; ++i)
                image.Pixels[i] = paletteMap[flat[i]];

            return image;
        }

        // ---- output --------------------------------------------------------

        /// <summary>The vgaimg the game reads: w, h, sx, sy, then w*h indices.</summary>
        public static byte[] ToVgaImage(DoomImage image)
        {
            if (image == null) return null;

            byte[] data = new byte[8 + image.W * image.H];

            BitConverter.GetBytes((ushort)image.W).CopyTo(data, 0);
            BitConverter.GetBytes((ushort)image.H).CopyTo(data, 2);
            BitConverter.GetBytes((short)image.Left).CopyTo(data, 4);
            BitConverter.GetBytes((short)image.Top).CopyTo(data, 6);

            Buffer.BlockCopy(image.Pixels, 0, data, 8, image.Pixels.Length);

            return data;
        }
    }
}
