//
// DPT.DOOM 2D — prototype: ESCAPADE, the platformer that came before Doom2D.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// ESCAPADE — a side-view platformer, ported from
    /// oldprog/TC/PROGS/ESCAPADE (GAME.C, OBJ.C, FIELD.C).
    ///
    /// This is the direct ancestor of Doom2D: same 64x64 tile field, same idea of
    /// a hero object with STAND/GO/FIRE states, same sprite-and-tile scrolling
    /// view. The original's artwork is used as-is (HERO1.VGA, TILE1.VGA), so the
    /// hero is the one drawn in 1995.
    ///
    /// Arrows walk, Up jumps, Space fires.
    /// </summary>
    internal sealed class EscapadeDemo : ProtoHost.Prototype
    {
        private const int FieldW = 64, FieldH = 64;
        private const int TileSize = 16;
        private const int ViewW = 320, ViewH = 200;

        private enum State { Stand, Go, Fire }

        private byte[] field;
        private VgaImage tileBack, tileFloor, tileWall;
        private VgaImage[] walk;
        private VgaImage[] fire;
        private VgaImage standLeft, standRight;

        private int heroX, heroY;          // pixels
        private int heroVX, heroVY;
        private bool facingRight = true;
        private bool onGround;
        private State state;
        private int animFrame, animTimer;
        private int camX, camY;

        public override string Id { get { return "escapade"; } }
        public override string Title { get { return "ESCAPADE — platformer"; } }
        public override string Source { get { return "oldprog/TC/PROGS/ESCAPADE"; } }

        public override string Description
        {
            get { return "Платформер 1995-го: поле 64x64, герой с анимациями, спрайты HERO1.VGA. Прямой предок Doom2D."; }
        }

        public override void Init()
        {
            VgaContainer.Container art = ProtoHost.LoadAsset("HERO1.VGA");
            VgaContainer.Container tiles = ProtoHost.LoadAsset("TILE1.VGA");

            if (tiles != null)
            {
                VgaContainer.ApplyPalette(tiles);
                tileBack = tiles.Get("back0a");
                tileFloor = tiles.Get("tile1a");
                tileWall = tiles.Get("tile2a");
            }

            if (art != null)
            {
                standLeft = art.Get("stL");
                standRight = art.Get("stR");

                walk = new[] { art.Get("go1R"), art.Get("go3R"), art.Get("go4R"), art.Get("go3R") };
                fire = new[] { art.Get("fi1R"), art.Get("fi2R") };
            }

            BuildField();
            ResetHero();
        }

        /// <summary>
        /// A small hand-made level: floor with a gap, a couple of platforms and a
        /// wall. The original loaded its field from a level file; a fixed one keeps
        /// the prototype self-contained.
        /// </summary>
        private void BuildField()
        {
            field = new byte[FieldW * FieldH];

            for (int y = 0; y < FieldH; ++y)
            {
                for (int x = 0; x < FieldW; ++x)
                {
                    byte t = 0;                               // empty

                    if (y >= 40) t = 1;                       // ground
                    if (y >= 40 && x >= 20 && x <= 24) t = 0; // a gap to jump over
                    if (y == 34 && x >= 10 && x <= 16) t = 1; // platform
                    if (y == 28 && x >= 30 && x <= 36) t = 1; // platform
                    if (y >= 30 && y < 40 && x == 44) t = 2;  // wall

                    field[y * FieldW + x] = t;
                }
            }
        }

        private void ResetHero()
        {
            heroX = 7 * TileSize;
            heroY = 38 * TileSize;
            heroVX = heroVY = 0;
            state = State.Stand;
        }

        private bool Solid(int tx, int ty)
        {
            if (tx < 0 || tx >= FieldW) return true;
            if (ty < 0) return false;
            if (ty >= FieldH) return true;

            return field[ty * FieldW + tx] != 0;
        }

        private void Update()
        {
            int speed = 2;
            int move = 0;

            if (Kbd.Down(Kbd.SDLK_LEFT) || Kbd.Down(Kbd.SDLK_KP4)) move = -speed;
            if (Kbd.Down(Kbd.SDLK_RIGHT) || Kbd.Down(Kbd.SDLK_KP6)) move = speed;

            if (move != 0)
            {
                facingRight = move > 0;
                state = State.Go;
            }
            else if (state == State.Go)
            {
                state = State.Stand;
            }

            if (Kbd.WasPressed(Kbd.SDLK_SPACE) && onGround)
            {
                heroVY = -7;
                onGround = false;
            }

            if (Kbd.WasPressed(Kbd.SDLK_RETURN) || Kbd.WasPressed(Kbd.SDLK_PAGEDOWN))
            {
                state = State.Fire;
                animFrame = 0;
                animTimer = 0;
            }

            // Horizontal move, one pixel at a time so a wall stops us cleanly.
            int step = Math.Sign(move);
            for (int i = 0; i < Math.Abs(move); ++i)
            {
                int nx = heroX + step;
                if (step != 0 && !Blocked(nx, heroY)) heroX = nx;
            }

            // Gravity and the floor.
            heroVY += 1;
            if (heroVY > 8) heroVY = 8;

            int ny = heroY + heroVY;

            if (heroVY > 0)
            {
                if (Blocked(heroX, ny + 19))
                {
                    // Land on the tile below.
                    heroY = ((ny + 19) / TileSize) * TileSize - 20;
                    heroVY = 0;
                    onGround = true;
                }
                else
                {
                    heroY = ny;
                    onGround = false;
                }
            }
            else if (heroVY < 0)
            {
                if (Blocked(heroX, ny))
                {
                    heroVY = 0;
                }
                else
                {
                    heroY = ny;
                }
            }

            if (heroY > FieldH * TileSize) ResetHero();

            // Animation.
            if (++animTimer >= 6)
            {
                animTimer = 0;
                ++animFrame;
            }

            if (state == State.Fire && animFrame >= fire.Length)
            {
                state = State.Stand;
                animFrame = 0;
            }

            // Camera keeps the hero in the middle, clamped to the field.
            camX = heroX - ViewW / 2;
            camY = heroY - ViewH / 2;

            if (camX < 0) camX = 0;
            if (camY < 0) camY = 0;
            if (camX > FieldW * TileSize - ViewW) camX = FieldW * TileSize - ViewW;
            if (camY > FieldH * TileSize - ViewH) camY = FieldH * TileSize - ViewH;
        }

        /// <summary>True when a 20x20 hero box at (x,y) overlaps a solid tile.</summary>
        private bool Blocked(int x, int y)
        {
            return Solid(x / TileSize, y / TileSize)
                || Solid((x + 19) / TileSize, y / TileSize)
                || Solid(x / TileSize, (y + 19) / TileSize)
                || Solid((x + 19) / TileSize, (y + 19) / TileSize);
        }

        public override void Frame(int frame)
        {
            Update();

            Vga.Clr(0, ViewW, 0, ViewH, 0);

            // Tiles.
            int firstX = camX / TileSize;
            int firstY = camY / TileSize;

            for (int ty = firstY; ty <= firstY + ViewH / TileSize + 1; ++ty)
            {
                for (int tx = firstX; tx <= firstX + ViewW / TileSize + 1; ++tx)
                {
                    if (tx < 0 || ty < 0 || tx >= FieldW || ty >= FieldH) continue;

                    byte t = field[ty * FieldW + tx];
                    VgaImage img = t == 1 ? tileFloor : (t == 2 ? tileWall : tileBack);
                    if (img == null) continue;

                    Vga.Pic(tx * TileSize - camX, ty * TileSize - camY, img);
                }
            }

            // Hero.
            VgaImage hero = null;

            if (state == State.Fire && fire != null)
                hero = fire[Math.Min(animFrame, fire.Length - 1)];
            else if (state == State.Go && walk != null)
                hero = walk[animFrame % walk.Length];
            else
                hero = standRight;

            if (hero != null)
            {
                int hx = heroX - camX;
                int hy = heroY - camY;

                if (facingRight) Vga.Spr(hx, hy, hero);
                else Vga.Spr2(hx, hy, hero);
            }

            ProtoHost.Text(4, 4, "ESCAPADE", 255);
            ProtoHost.Text(4, 190, "ARROWS - WALK   UP - JUMP   SPACE - FIRE", 255);
        }
    }
}
