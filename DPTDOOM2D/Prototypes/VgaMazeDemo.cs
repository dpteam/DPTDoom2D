//
// DPT.DOOM 2D — prototype: VGA Maze.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// VGA MAZE — a scrolling tile maze, ported from
    /// oldprog/TC/PROGS/VGAMAZE (GAME.C, OBJ.C, FIELD.ASM).
    ///
    /// The original filled a 64x64 field with three random tile types and put one
    /// object in it; the view scrolled to follow that object. It is the first
    /// program in the archive that has the shape Doom2D kept: a tile field, an
    /// object list, a camera and a per-tick update.
    ///
    /// Arrows move, Space regenerates the maze.
    /// </summary>
    internal sealed class VgaMazeDemo : ProtoHost.Prototype
    {
        private const int FieldW = 64, FieldH = 64;
        private const int TileSize = 16;

        private byte[] field;
        private VgaImage[] tiles;
        private VgaImage[] man;
        private VgaImage back;

        private int manX, manY;
        private bool facingRight = true;
        private int animFrame, animTimer;
        private bool moving;

        public override string Id { get { return "vgamaze"; } }
        public override string Title { get { return "VGA MAZE — tile field"; } }
        public override string Source { get { return "oldprog/TC/PROGS/VGAMAZE"; } }

        public override string Description
        {
            get { return "Лабиринт из случайных тайлов 64x64 со скроллом. Первая программа в архиве с формой будущего Doom2D."; }
        }

        public override void Init()
        {
            VgaContainer.Container art = ProtoHost.LoadAsset("TILE1.VGA");
            VgaContainer.Container hero = ProtoHost.LoadAsset("MAN.VGA");

            if (art != null)
            {
                VgaContainer.ApplyPalette(art);
                back = art.Get("back0a");

                tiles = new[] { art.Get("back0a"), art.Get("tile1a"), art.Get("tile2a") };
            }

            if (hero != null)
            {
                man = new[]
                {
                    hero.Get("standR"), hero.Get("standL"),
                    hero.Get("go1R"), hero.Get("go2R"),
                    hero.Get("go1L"), hero.Get("go2L")
                };
            }

            Generate();
        }

        private void Generate()
        {
            field = new byte[FieldW * FieldH];

            for (int i = 0; i < field.Length; ++i)
                field[i] = (byte)Glob.Rand(3);

            manX = 11 * TileSize;
            manY = 5 * TileSize;
            animFrame = 0;
        }

        private bool Solid(int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= FieldW || ty >= FieldH) return true;
            return field[ty * FieldW + tx] == 2;
        }

        private void Update()
        {
            int move = 0;

            if (Kbd.Down(Kbd.SDLK_LEFT) || Kbd.Down(Kbd.SDLK_KP4)) move = -2;
            if (Kbd.Down(Kbd.SDLK_RIGHT) || Kbd.Down(Kbd.SDLK_KP6)) move = 2;

            moving = move != 0;

            if (moving)
            {
                facingRight = move > 0;

                int nx = manX + move;
                if (!Solid(nx / TileSize, manY / TileSize)) manX = nx;
            }

            if (Kbd.Down(Kbd.SDLK_UP) || Kbd.Down(Kbd.SDLK_KP8))
            {
                int ny = manY - 2;
                if (!Solid(manX / TileSize, ny / TileSize)) manY = ny;
                moving = true;
            }

            if (Kbd.Down(Kbd.SDLK_DOWN) || Kbd.Down(Kbd.SDLK_KP5))
            {
                int ny = manY + 2;
                if (!Solid(manX / TileSize, (ny + 15) / TileSize)) manY = ny;
                moving = true;
            }

            if (Kbd.WasPressed(Kbd.SDLK_SPACE)) Generate();

            if (++animTimer >= 5)
            {
                animTimer = 0;
                ++animFrame;
            }
        }

        public override void Frame(int frame)
        {
            Update();

            int camX = manX - 160;
            int camY = manY - 100;

            if (camX < 0) camX = 0;
            if (camY < 0) camY = 0;
            if (camX > FieldW * TileSize - 320) camX = FieldW * TileSize - 320;
            if (camY > FieldH * TileSize - 200) camY = FieldH * TileSize - 200;

            Vga.Clr(0, 320, 0, 200, 0);

            int firstX = camX / TileSize;
            int firstY = camY / TileSize;

            for (int ty = firstY; ty <= firstY + 200 / TileSize + 1; ++ty)
            {
                for (int tx = firstX; tx <= firstX + 320 / TileSize + 1; ++tx)
                {
                    if (tx < 0 || ty < 0 || tx >= FieldW || ty >= FieldH) continue;

                    VgaImage img = tiles != null ? tiles[field[ty * FieldW + tx]] : null;
                    if (img == null) continue;

                    Vga.Pic(tx * TileSize - camX, ty * TileSize - camY, img);
                }
            }

            if (man != null)
            {
                VgaImage img;

                if (!moving)
                    img = facingRight ? man[0] : man[1];
                else
                    img = facingRight ? man[2 + (animFrame % 2)] : man[4 + (animFrame % 2)];

                if (img != null)
                {
                    int sx = manX - camX;
                    int sy = manY - camY;

                    if (facingRight) Vga.Spr(sx, sy, img);
                    else Vga.Spr2(sx, sy, img);
                }
            }

            ProtoHost.Text(4, 4, "VGA MAZE", 255);
            ProtoHost.Text(4, 190, "ARROWS - MOVE   SPACE - NEW MAZE", 255);
        }
    }
}
