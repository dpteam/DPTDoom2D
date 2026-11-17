//
// DPT.DOOM 2D — map rendering for the editor.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Editor
{
    /// <summary>
    /// Draws the level the way the DOS editor did, through W_draw.
    ///
    /// The numbers are the original's: a cell is 8x8 pixels (CELW/CELH in VIEW.H),
    /// the view is 200x98 at the top-left of the screen, and the level is always
    /// 100x100 cells. The camera is the original's (w_x, w_y) — the *centre* of the
    /// view in map pixels, not its corner — because that is what the scroll bars and
    /// the mouse handlers both speak.
    ///
    /// Layer order is the original's too, and it matters: back walls, then things,
    /// then front walls, then cell types, then switches. A thing standing on a front
    /// wall is hidden by it, and a type marker sits on top of everything.
    /// </summary>
    internal static class MapCanvas
    {
        /// <summary>One cell is 8x8 pixels: CELW and CELH from VIEW.H.</summary>
        public const int CellSize = 8;

        /// <summary>The level is always 100x100 cells: FLDW and FLDH.</summary>
        public const int FieldWidth = 100, FieldHeight = 100;

        /// <summary>Map pixels, not cells: FLDW*CELW.</summary>
        public const int MapWidth = FieldWidth * CellSize;
        public const int MapHeight = FieldHeight * CellSize;

        /// <summary>WD/2 and HT/2 from VIEW.CPP: half the 200x98 view.</summary>
        public const int ViewHalfW = 100, ViewHalfH = 49;

        /// <summary>MAXX-WD/2 and MAXY-HT/2, the divisors W_draw divides by.</summary>
        private const int SkyRangeX = MapWidth - ViewHalfW * 2;
        private const int SkyRangeY = MapHeight - ViewHalfH * 2;

        /// <summary>
        /// The type layer's colours, tctab from MISCA.ASM.
        ///
        /// One entry per cell type, drawn as a 4x4 block inset by two pixels — small
        /// enough to leave the texture under it readable, big enough to see.
        /// </summary>
        private static readonly byte[] TypeTable =
        {
            0x00, 0x54, 0xA2, 0x72, 0x60, 0xC8, 0xDC, 0xD6, 0xFB, 0x48, 0x98
        };

        private static readonly Dictionary<int, VgaImage> textures = new Dictionary<int, VgaImage>();

        private static VgaImage sky;
        private static int skyType = -1;

        /// <summary>
        /// Draws the level into a viewport.
        ///
        /// <paramref name="wX"/>/<paramref name="wY"/> are the camera centre in map
        /// pixels, exactly like the original's globals of the same name. The viewport
        /// is normally 200x98 at (0,1); the whole-level render uses the full map.
        /// </summary>
        public static void Draw(MapFile map, int wX, int wY,
            int viewX, int viewY, int viewW, int viewH,
            bool showBack, bool showFront, bool showTypes, bool showThings, bool showSwitch,
            int selectedThing, int selectedSwitch,
            bool dragging, int dragX1, int dragY1, int dragX2, int dragY2)
        {
            Vga.SetRect(viewX, viewW, viewY, viewH);

            int halfW = viewW / 2;
            int halfH = viewH / 2;

            DrawSky(map, wX, wY, viewX, viewY);

            if (showBack) DrawField(map, map.Back, wX, wY, viewX + halfW, viewY + halfH, viewW, viewH);
            if (showThings) DrawThings(map, wX, wY, viewX + halfW, viewY + halfH, selectedThing);
            if (showFront) DrawField(map, map.Front, wX, wY, viewX + halfW, viewY + halfH, viewW, viewH);
            if (showTypes) DrawTypes(map, wX, wY, viewX + halfW, viewY + halfH, viewW, viewH);
            if (showSwitch) DrawSwitches(map, wX, wY, viewX + halfW, viewY + halfH, selectedSwitch);

            if (dragging)
                DrawSelection(dragX1, dragY1, dragX2, dragY2, wX, wY, viewX + halfW, viewY + halfH, viewW, viewH);

            Vga.SetRect(0, Vga.SCRW, 0, Vga.SCRH);
        }

        /// <summary>
        /// W_draw's horizon.
        ///
        /// The original calls `V_pic(127-(w_x-100)*56/600, 123-(w_y-49)*28/702, horiz)`,
        /// and those 127 and 123 look like magic constants. They are not: they are the
        /// **hot spot of the 1996 sky picture**, which is 256x128 with `sx=127, sy=123`.
        /// V_pic subtracts the hot spot, so the sky's top-left lands at
        /// `(127-127, 123-123) = (0,0)` and the whole view is covered.
        ///
        /// The data set this port ships has a *different* sky — 256x256 with no hot
        /// spot at all — so the same call puts its top-left at (127,123) and only the
        /// right-hand part of the view gets any sky. That is the "renders strangely"
        /// this fixes: the position is computed from the picture's own hot spot, so it
        /// works with either data set.
        ///
        /// The parallax itself is the original's, character for character: the sky
        /// drifts by at most 56 pixels across the level horizontally and 28 vertically.
        /// </summary>
        private static void DrawSky(MapFile map, int wX, int wY, int viewX, int viewY)
        {
            VgaImage image = Sky(map);

            if (image == null) return;

            int x = viewX - (wX - ViewHalfW) * 56 / SkyRangeX;
            int y = viewY - (wY - ViewHalfH) * 28 / SkyRangeY;

            // Vga.Pic subtracts the hot spot; add it back so the top-left stays where
            // it was computed, whatever the picture's own offset happens to be.
            Vga.Pic(x + image.SX, y + image.SY, image);
        }

        private static VgaImage Sky(MapFile map)
        {
            int type = map.Sky > 0 ? map.Sky : 1;

            if (sky != null && skyType == type) return sky;

            int id = Wad.FindRes("RSKY" + type);

            sky = id >= 0 ? Res.Img(id) : null;
            skyType = type;

            return sky;
        }

        /// <summary>
        /// Z_drawfld: one texture stamped per cell, at the cell's top-left corner.
        ///
        /// Not one 8x8 piece per cell: the original calls V_pic with the whole
        /// 16x16 texture, so neighbouring cells overlap and a wall reads as one
        /// continuous surface instead of a grid of tiles.
        ///
        /// A cell whose wall is `_WATER_n` is not a picture at all — it is a request to
        /// darken whatever is already there, which is how the game animates water with
        /// a palette remap instead of a sprite. The original does the same in
        /// Z_drawfld's `@@water` branch: `clrmap + (value+7)*256` over the cell. Skipping
        /// those cells leaves the sky showing through a river.
        /// </summary>
        private static void DrawField(MapFile map, byte[] field, int wX, int wY,
            int originX, int originY, int viewW, int viewH)
        {
            for (int cy = 0; cy < FieldHeight; ++cy)
            {
                int sy = cy * CellSize - wY + originY;

                if (sy > originY + viewH || sy + CellSize < originY - CellSize) continue;

                for (int cx = 0; cx < FieldWidth; ++cx)
                {
                    int index = field[cy * FieldWidth + cx];
                    if (index == 0) continue;

                    int sx = cx * CellSize - wX + originX;

                    if (sx > originX + viewW || sx + CellSize < originX - CellSize) continue;

                    int water = WaterRow(map, index);

                    if (water >= 0)
                    {
                        RemapCell(sx, sy, water);
                        continue;
                    }

                    VgaImage image = Texture(map, index);

                    if (image != null) Vga.Pic(sx, sy, image);
                }
            }
        }

        /// <summary>
        /// The clrmap row a `_WATER_n` wall asks for, or -1 for an ordinary wall.
        ///
        /// The original stores the water level in the wall slot as a small integer
        /// (`walp[i] = n[7]-'0'+1`) and then adds 7 to index clrmap, so "_WATER_0"
        /// selects row 8. The table has twelve rows; anything past the end would read
        /// past the array in C, so it is clamped here.
        /// </summary>
        private static int WaterRow(MapFile map, int index)
        {
            string name = map.WallName[index];

            if (string.IsNullOrEmpty(name)) return -1;
            if (!name.StartsWith("_WATER_", StringComparison.OrdinalIgnoreCase)) return -1;

            int level = name.Length > 7 ? name[7] - '0' : 0;
            if (level < 0 || level > 9) level = 0;

            int row = level + 8;

            return row < 12 ? row : 11;
        }

        private static readonly byte[][] ClrMapRows = BuildClrMapRows();

        /// <summary>
        /// The twelve clrmap rows, copied out once.
        ///
        /// Vga.MapPixel takes a 256-byte table rather than a row index, and the
        /// palette keeps all twelve rows in one array — so the rows are sliced here
        /// instead of on every pixel of every water cell.
        /// </summary>
        private static byte[][] BuildClrMapRows()
        {
            var rows = new byte[12][];

            for (int i = 0; i < 12; ++i)
            {
                rows[i] = new byte[256];
                Array.Copy(Palette.ClrMap, i * 256, rows[i], 0, 256);
            }

            return rows;
        }

        /// <summary>V_remap_rect over one cell: the water shading pass.</summary>
        private static void RemapCell(int x, int y, int row)
        {
            byte[] cmap = ClrMapRows[row];

            for (int py = y; py < y + CellSize; ++py)
                for (int px = x; px < x + CellSize; ++px)
                    Vga.MapPixel(px, py, cmap);
        }

        /// <summary>tctab, the type layer's colours, shared by the view and the minimap.</summary>
        public static byte TypeColor(int type)
        {
            return type > 0 && type < TypeTable.Length ? TypeTable[type] : (byte)0xFB;
        }

        private static VgaImage Texture(MapFile map, int index)
        {
            VgaImage found;

            if (textures.TryGetValue(index, out found)) return found;

            VgaImage image = null;
            string name = map.WallName[index];

            if (!string.IsNullOrEmpty(name) && !name.StartsWith("_WATER_", StringComparison.Ordinal))
            {
                int id = Wad.FindRes(name);
                if (id >= 0) image = Res.Img(id);
            }

            textures[index] = image;
            return image;
        }

        /// <summary>Drops the texture cache: a new map may reuse an index for another wall.</summary>
        public static void Forget()
        {
            textures.Clear();
            sky = null;
            skyType = -1;
        }

        /// <summary>Z_drawfldt: a 4x4 colour block per cell, inset by two pixels.</summary>
        private static void DrawTypes(MapFile map, int wX, int wY,
            int originX, int originY, int viewW, int viewH)
        {
            for (int cy = 0; cy < FieldHeight; ++cy)
            {
                int sy = cy * CellSize - wY + originY;

                if (sy + 2 > originY + viewH || sy + 6 < originY) continue;

                for (int cx = 0; cx < FieldWidth; ++cx)
                {
                    int type = map.Type[cy * FieldWidth + cx];
                    if (type == 0) continue;

                    int sx = cx * CellSize - wX + originX;

                    if (sx + 2 > originX + viewW || sx + 6 < originX) continue;

                    byte color = type < TypeTable.Length ? TypeTable[type] : (byte)0xFB;

                    Vga.Clr(sx + 2, 4, sy + 2, 4, color);
                }
            }
        }

        /// <summary>
        /// TH_draw: one sprite per thing, players and DM starts in their own colour,
        /// and a bracket around the selected one.
        ///
        /// The bracket is four one-pixel lines in 0xAD, drawn at +-5 pixels — the
        /// original's way of saying "this is what the tab is editing".
        /// </summary>
        private static void DrawThings(MapFile map, int wX, int wY,
            int originX, int originY, int selected)
        {
            for (int i = 0; i < map.Things.Count; ++i)
            {
                MapThing thing = map.Things[i];

                int sx = thing.X - wX + originX;
                int sy = thing.Y - wY + originY;

                if (sx < -64 || sx > 264 || sy < -64 || sy > 162) continue;

                VgaImage sprite = ThingSprite(thing.Type);

                if (sprite != null) Vga.Spr(sx, sy, sprite);

                if (i != selected) continue;

                Vga.Clr(sx - 5, 11, sy - 10, 1, 0xAD);
                Vga.Clr(sx - 5, 11, sy, 1, 0xAD);
                Vga.Clr(sx - 5, 1, sy - 9, 9, 0xAD);
                Vga.Clr(sx + 5, 1, sy - 9, 9, 0xAD);
            }
        }

        /// <summary>
        /// SW_draw: four dots marking a switch, in 0x70 or 0xA0 when selected, and
        /// four more marking where it acts.
        /// </summary>
        private static void DrawSwitches(MapFile map, int wX, int wY,
            int originX, int originY, int selected)
        {
            for (int i = 0; i < map.Switches.Count; ++i)
            {
                MapSwitch sw = map.Switches[i];

                int x = sw.X * CellSize + 1 - wX + originX;
                int y = sw.Y * CellSize + 1 - wY + originY;

                byte color = (byte)(i == selected ? 0xA0 : 0x70);

                Vga.PutPixel(x, y, color);
                Vga.PutPixel(x + 5, y, color);
                Vga.PutPixel(x + 5, y + 5, color);
                Vga.PutPixel(x, y + 5, color);

                if (i != selected) continue;

                int tx = sw.A * CellSize + 1 - wX + originX;
                int ty = sw.B * CellSize + 1 - wY + originY;

                Vga.PutPixel(tx + 2, ty, 0xB0);
                Vga.PutPixel(tx + 3, ty + 5, 0xB0);
                Vga.PutPixel(tx, ty + 3, 0xB0);
                Vga.PutPixel(tx + 5, ty + 2, 0xB0);
            }
        }

        private static readonly byte[] SelectionMap = SelectionCmap();

        /// <summary>
        /// clrmap row 6, the one W_draw uses for the rectangle being dragged.
        ///
        /// Copied out of the table once because MapPixel takes a 256-byte slice and
        /// the palette keeps all twelve rows in one array.
        /// </summary>
        private static byte[] SelectionCmap()
        {
            byte[] row = new byte[256];
            Array.Copy(Palette.ClrMap, 6 * 256, row, 0, 256);
            return row;
        }

        /// <summary>
        /// The rectangle being dragged, remapped with clrmap[6] the way W_draw does
        /// it — a darkening pass, so the area stays readable while it is chosen.
        /// </summary>
        private static void DrawSelection(int x1, int y1, int x2, int y2,
            int wX, int wY, int originX, int originY, int viewW, int viewH)
        {
            int left = Math.Min(x1, x2), right = Math.Max(x1, x2);
            int top = Math.Min(y1, y2), bottom = Math.Max(y1, y2);

            int sx = left * CellSize - wX + originX;
            int sy = top * CellSize - wY + originY;

            int w = (right - left) * CellSize + CellSize;
            int h = (bottom - top) * CellSize + CellSize;

            if (sx > originX + viewW || sy > originY + viewH) return;
            if (sx + w < originX || sy + h < originY) return;

            for (int py = Math.Max(originY, sy); py < Math.Min(originY + viewH, sy + h); ++py)
                for (int px = Math.Max(originX, sx); px < Math.Min(originX + viewW, sx + w); ++px)
                    Vga.MapPixel(px, py, SelectionMap);
        }

        /// <summary>
        /// The first sprite frame of a thing. Monsters come from the actor registry,
        /// items from items.c's tables; the editor only needs one picture each.
        /// </summary>
        public static VgaImage ThingSprite(int type)
        {
            string name = ThingSpriteName(type);
            if (string.IsNullOrEmpty(name)) return null;

            int id = Wad.TryGetSprId(name, 0, 0);
            return id < 0 ? null : Res.Img(id);
        }

        public static string ThingSpriteName(int type)
        {
            // 200..219: monsters, in the same order as the actor registry.
            if (type >= MapThingType.Demon && type < MapThingType.Demon + Actors.TypeCount)
                return Actors.Sprite[type - MapThingType.Demon];

            // 100..135: items, in items.c's order.
            if (type >= MapThingType.Clip && type < MapThingType.Clip + ItemSprites.Length)
                return ItemSprites[type - MapThingType.Clip];

            return null;
        }

        /// <summary>items.c's sprite names, in the order the map format numbers them.</summary>
        public static readonly string[] ItemSprites =
        {
            "CLIP", "SHEL", "ROCK", "CELL", "AMMO", "SBOX", "BROK", "CELP",
            "STIM", "MEDI", "BPAK",
            "CSAW", "SHOT", "SGN2", "MGUN", "LAUN", "PLAS", "BFUG",
            "SOUL", "SMRT", "SMGT", "SMBT",
            "MEGA", "MEGA", "MEGA", "MEGA",
            "PINV", "PINV", "PINV", "PINV",
            "AQUA", "KEYR", "KEYG", "KEYB", "SUIT",
            "GOR1"
        };

        // ---------------------------------------------------------------- bitmap

        /// <summary>
        /// Writes the framebuffer as a 24-bit BMP.
        ///
        /// No System.Drawing: the editor is a single exe with no dependencies, and a
        /// BMP writer is twenty lines. The same code the game's screenshot uses, so
        /// the two pictures can be compared byte for byte.
        /// </summary>
        public static void WriteBmp(string path, int width, int height)
        {
            int rowSize = (width * 3 + 3) & ~3;
            int dataSize = rowSize * height;

            using (FileStream fs = File.Create(path))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((byte)'B'); bw.Write((byte)'M');
                bw.Write(54 + dataSize); bw.Write(0); bw.Write(54);
                bw.Write(40); bw.Write(width); bw.Write(height);
                bw.Write((short)1); bw.Write((short)24);
                bw.Write(0); bw.Write(dataSize);
                bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);

                byte[] src = Vga.Buffer;
                byte[] row = new byte[rowSize];

                for (int y = height - 1; y >= 0; --y)
                {
                    int at = 0;

                    for (int x = 0; x < width; ++x)
                    {
                        int i = src[y * width + x];
                        row[at++] = (byte)Math.Min(255, Palette.Std[i * 3 + 2] * 4);
                        row[at++] = (byte)Math.Min(255, Palette.Std[i * 3 + 1] * 4);
                        row[at++] = (byte)Math.Min(255, Palette.Std[i * 3] * 4);
                    }

                    bw.Write(row, 0, rowSize);
                }
            }
        }
    }

    /// <summary>
    /// The map format's thing numbers. Game/Items.cs owns the same constants, but it
    /// pulls in the whole item simulation; the editor only needs the numbers.
    /// </summary>
    internal static class MapThingType
    {
        public const short Plr1 = 1, Plr2 = 2, DmStart = 3;
        public const short Clip = 100;
        public const short Demon = 200;
    }
}
