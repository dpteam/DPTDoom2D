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
    /// Draws a map region into the 8-bit framebuffer with the game's own textures
    /// and sprites.
    ///
    /// The original DOS editor did exactly this: it loaded the same WAD and called
    /// Z_drawfld, so the level looked like the level while it was being built. The
    /// only layer the game never draws is the collision map, which is shown as a
    /// stipple on top — a mapper still has to see where a wall blocks movement.
    /// </summary>
    internal static class MapCanvas
    {
        /// <summary>One map cell is 16x16 pixels, as in the engine.</summary>
        public const int CellSize = 16;

        private static readonly Dictionary<int, VgaImage> textures = new Dictionary<int, VgaImage>();

        /// <summary>
        /// Draws the map through a camera into a rectangle of the framebuffer.
        ///
        /// <paramref name="camX"/>/<paramref name="camY"/> are the map pixel that
        /// appears at the top-left of the viewport.
        /// </summary>
        public static void Draw(MapFile map, int camX, int camY,
            int viewX, int viewY, int viewW, int viewH,
            bool showBack, bool showFront, bool showTypes)
        {
            Vga.SetRect(viewX, viewW, viewY, viewH);

            for (int y = 0; y < MapFile.Height; ++y)
            {
                for (int x = 0; x < MapFile.Width; ++x)
                {
                    int at = y * MapFile.Width + x;
                    int px = x * CellSize - camX + viewX;
                    int py = y * CellSize - camY + viewY;

                    if (px > viewX + viewW || py > viewY + viewH) continue;
                    if (px + CellSize < viewX || py + CellSize < viewY) continue;

                    if (showBack) DrawWall(map, map.Back[at], px, py);
                }
            }

            DrawThings(map, camX, camY, viewX, viewY, viewW, viewH);

            if (showFront)
            {
                for (int y = 0; y < MapFile.Height; ++y)
                {
                    for (int x = 0; x < MapFile.Width; ++x)
                    {
                        int at = y * MapFile.Width + x;
                        int px = x * CellSize - camX + viewX;
                        int py = y * CellSize - camY + viewY;

                        if (px > viewX + viewW || py > viewY + viewH) continue;
                        if (px + CellSize < viewX || py + CellSize < viewY) continue;

                        DrawWall(map, map.Front[at], px, py);
                    }
                }
            }

            if (showTypes) DrawTypes(map, camX, camY, viewX, viewY, viewW, viewH);

            Vga.SetRect(0, Vga.SCRW, 0, Vga.SCRH);
        }

        private static void DrawWall(MapFile map, int index, int px, int py)
        {
            if (index == 0) return;

            VgaImage image = Texture(map, index);
            if (image == null) return;

            Vga.Pic(px, py, image);
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
        }

        /// <summary>
        /// The collision layer. A sparse stipple, not a solid fill: the texture
        /// under it stays readable, which is the point of drawing the real artwork.
        /// </summary>
        private static void DrawTypes(MapFile map, int camX, int camY,
            int viewX, int viewY, int viewW, int viewH)
        {
            for (int y = 0; y < MapFile.Height; ++y)
            {
                for (int x = 0; x < MapFile.Width; ++x)
                {
                    byte type = map.Type[y * MapFile.Width + x];
                    if (type == 0) continue;

                    byte color = TypeColor(type);
                    int baseX = x * CellSize - camX + viewX;
                    int baseY = y * CellSize - camY + viewY;

                    if (baseX > viewX + viewW || baseY > viewY + viewH) continue;
                    if (baseX + CellSize < viewX || baseY + CellSize < viewY) continue;

                    for (int py = 0; py < CellSize; py += 2)
                    {
                        for (int px = 0; px < CellSize; px += 2)
                        {
                            Vga.PutPixel(baseX + px, baseY + py, color);
                        }
                    }
                }
            }
        }

        private static byte TypeColor(int type)
        {
            switch (type)
            {
                case Game.Cell.Wall: return 0xB0;
                case Game.Cell.WallSwapped: return 0x90;
                case Game.Cell.Water: return 0xC0;
                case Game.Cell.WaterTop: return 0xC1;
                case Game.Cell.Acid: return 0xA0;
                case Game.Cell.AcidDeep: return 0xA1;
                case Game.Cell.Lava: return 0xA2;
                case Game.Cell.Trap: return 0x40;
                case Game.Cell.LiftUp: return 0x70;
                case Game.Cell.LiftDown: return 0x71;
                default: return 0x50;
            }
        }

        private static void DrawThings(MapFile map, int camX, int camY,
            int viewX, int viewY, int viewW, int viewH)
        {
            for (int i = 0; i < map.Things.Count; ++i)
            {
                MapThing thing = map.Things[i];
                VgaImage sprite = ThingSprite(thing.Type);

                if (sprite != null)
                {
                    // Things are stored by their feet, as the game draws them.
                    int px = thing.X - sprite.W / 2 - camX + viewX;
                    int py = thing.Y - sprite.H - camY + viewY;

                    Vga.Spr(px, py, sprite);
                }
                else
                {
                    int px = thing.X - camX + viewX;
                    int py = thing.Y - camY + viewY;
                    Vga.Clr(px - 2, 5, py - 5, 5, ThingColor(thing.Type));
                }
            }
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

        private static byte ThingColor(int type)
        {
            if (type == MapThingType.Plr1) return 0x70;
            if (type == MapThingType.Plr2) return 0x60;
            if (type == MapThingType.DmStart) return 0x20;
            if (type >= MapThingType.Demon) return 0xB0;
            return 0x90;
        }

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
