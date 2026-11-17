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
        /// W_draw's horizon: RSKY1..3 stamped at a parallax offset.
        ///
        /// The constants are the original's, character for character:
        /// `127-(w_x-100)*56/(FLDW*CELW-200)` and `123-(w_y-49)*28/(FLDH*CELH-98)`.
        /// The sky is a 256x256 picture hung near the bottom of the view, so at the
        /// centre of a level it is off-screen entirely — which is what the DOS
        /// editor showed too, and why an empty top of the view is not a bug.
        /// </summary>
        private static void DrawSky(MapFile map, int wX, int wY, int viewX, int viewY)
        {
            VgaImage image = Sky(map);

            if (image == null) return;

            int x = viewX + 127 - (wX - ViewHalfW) * 56 / SkyRangeX;
            int y = viewY + 122 - (wY - ViewHalfH) * 28 / SkyRangeY;

            Vga.Pic(x, y, image);
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

                    VgaImage image = Texture(map, index);

                    if (image != null) Vga.Pic(sx, sy, image);
                }
            }
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
