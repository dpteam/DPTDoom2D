//
// DPT.DOOM 2D — AIDooM2D: the levels.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Builds the nineteen levels.
    ///
    /// The generator is deliberately simple and legible: a grid of rooms carved out of
    /// solid rock, corridors between neighbours, a start in the first room and an exit
    /// in the last. That is enough to be a game — you can walk from one end to the
    /// other, monsters have somewhere to stand, and every level is different — and it is
    /// far more robust than trying to grow something organic on a 100x100 grid.
    ///
    /// Two things about Doom2D's map format shape the result:
    ///
    ///  * **There is no floor tile.** A cell with an empty back layer shows the sky, so
    ///    a room is "carved" by clearing the back layer rather than by painting a floor.
    ///  * **A wall is a cell whose type is solid.** The back layer only says what it
    ///    looks like; the type layer is what stops the player.
    ///
    /// Everything is derived from the seed, so `--genwad 7` produces the same nineteen
    /// levels on every machine.
    /// </summary>
    internal static class AiLevels
    {
        /// <summary>The level is always 100x100 cells.</summary>
        private const int Size = MapFile.Width;

        /// <summary>Rooms per side: nine rooms per level, joined in a grid.</summary>
        private const int Grid = 3;

        /// <summary>The wall names a generated level uses, in slot order.</summary>
        public static readonly string[] WallNames =
        {
            "",             // slot 0 is "no wall"
            "AIWALL01",     // brick
            "AIWALL02",     // stone
            "AIWALL03",     // metal
            "AIWALL04",     // panel
            "AIDOOR01",     // door
            "AIDOOR02",     // door, second look
            "AIEXIT",       // exit sign
            "AITRAP",       // trap
            "WALL22_1",     // animated: the engine's own set
            "WALL23_1",
            "WALL23_2",
            "WALL58_1",
            "WALL58_2",
            "WALL58_3",
            "W73A_1",
            "W73A_2",
            "RP2_1",
            "RP2_2",
            "RP2_3",
            "RP2_4",
            "AIWALL05",     // stone variant
            "AIWALL06"      // brick variant
        };

        /// <summary>
        /// The animated walls the engine loads by name at startup.
        ///
        /// `World.Init` calls `Wad.GetResId` on these, which throws when a lump is
        /// missing — so a data set without them does not boot at all. They are also the
        /// reason the list above includes names that no generated level has to use.
        /// </summary>
        public static readonly string[] AnimatedWalls =
        {
            "WALL22_1", "WALL23_1", "WALL23_2",
            "WALL58_1", "WALL58_2", "WALL58_3",
            "W73A_1", "W73A_2",
            "RP2_1", "RP2_2", "RP2_3", "RP2_4"
        };

        /// <summary>How many levels a set carries; the game's campaign is nineteen.</summary>
        public const int Count = 19;

        /// <summary>
        /// One level.
        ///
        /// <paramref name="index"/> is 1-based so it matches the lump name MAP01..MAP19.
        /// </summary>
        public static MapFile Build(uint seed, int index)
        {
            var map = new MapFile();
            var rnd = new AiArt.Rand(AiArt.Hash("map" + index, seed));

            for (int i = 0; i < WallNames.Length; ++i)
            {
                map.WallName[i] = WallNames[i];
                map.WallType[i] = 0;            // every generated wall is opaque
            }

            // Start from solid rock: every cell is a wall until it is carved.
            for (int i = 0; i < map.Back.Length; ++i)
            {
                map.Back[i] = (byte)(1 + rnd.Below(4));   // slots 1..4: the plain materials
                map.Type[i] = Game.Cell.Wall;
            }

            // The rooms, in grid order. Room 0 gets the player, the last one the exit.
            var rooms = new List<int[]>(Grid * Grid);

            for (int gy = 0; gy < Grid; ++gy)
            {
                for (int gx = 0; gx < Grid; ++gx)
                {
                    // Each room lives inside its own cell of the grid, with a margin so
                    // two rooms never touch and the rock between them stays.
                    int cellX = gx * Size / Grid;
                    int cellY = gy * Size / Grid;
                    int cellW = Size / Grid;
                    int cellH = Size / Grid;

                    int w = 10 + rnd.Below(cellW - 16);
                    int h = 10 + rnd.Below(cellH - 16);

                    int x = cellX + 3 + rnd.Below(cellW - w - 6);
                    int y = cellY + 3 + rnd.Below(cellH - h - 6);

                    Carve(map, x, y, w, h);
                    rooms.Add(new[] { x, y, w, h });
                }
            }

            // Corridors: every room is joined to its right and lower neighbour, which
            // makes the whole grid reachable without a path-finding pass.
            for (int gy = 0; gy < Grid; ++gy)
            {
                for (int gx = 0; gx < Grid; ++gx)
                {
                    int here = gy * Grid + gx;

                    if (gx + 1 < Grid) Connect(map, rooms[here], rooms[here + 1], true, rnd);
                    if (gy + 1 < Grid) Connect(map, rooms[here], rooms[here + Grid], false, rnd);
                }
            }

            // A door frame where two rooms meet: the corridor is narrowed to one cell
            // and that cell becomes a switch, which is how Doom2D makes a door.
            for (int i = 0; i < rooms.Count; ++i)
            {
                if (rnd.Below(100) >= 35) continue;

                int[] room = rooms[i];
                int cx = room[0] + room[2] / 2;
                int cy = room[1] + room[3] / 2;

                var sw = new MapSwitch();
                sw.X = (byte)cx;
                sw.Y = (byte)cy;
                sw.A = (byte)cx;
                sw.B = (byte)cy;
                sw.Type = Game.SwType.Door;
                sw.Flags = 0x03;                // player and monster can press it
                map.Switches.Add(sw);
            }

            // Water in one room: the front layer names it, the engine tints it.
            int wetRoom = 1 + rnd.Below(rooms.Count - 2);
            Flood(map, rooms[wetRoom], rnd);

            // The player, in the first room.
            AddThing(map, rooms[0], Game.ThingType.Plr1, rnd);

            // The exit, in the last room, as a switch: Doom2D ends a level on a trigger.
            int[] last = rooms[rooms.Count - 1];
            var exit = new MapSwitch();
            exit.X = (byte)(last[0] + last[2] / 2);
            exit.Y = (byte)(last[1] + last[3] / 2);
            exit.A = exit.X;
            exit.B = exit.Y;
            exit.Type = Game.SwType.Exit;
            exit.Flags = 0x03;
            map.Switches.Add(exit);

            // Monsters and pickups, spread over the rooms that are not the start.
            int monsters = 3 + rnd.Below(6);
            int items = 5 + rnd.Below(8);

            for (int i = 0; i < monsters; ++i)
            {
                int room = 1 + rnd.Below(rooms.Count - 1);
                AddThing(map, rooms[room], (short)(Game.ThingType.Demon + rnd.Below(12)), rnd);
            }

            for (int i = 0; i < items; ++i)
            {
                int room = rnd.Below(rooms.Count);
                AddThing(map, rooms[room], (short)(Game.ThingType.Clip + rnd.Below(36)), rnd);
            }

            map.Music = "AIMUS" + (1 + (index - 1) % AiMusic.Count);
            map.Sky = 1 + (index - 1) % 2;
            map.Source = "AIDooM2D";

            return map;
        }

        /// <summary>Clears a rectangle: no wall texture and nothing solid.</summary>
        private static void Carve(MapFile map, int x, int y, int w, int h)
        {
            for (int cy = y; cy < y + h; ++cy)
            {
                if (cy < 0 || cy >= Size) continue;

                for (int cx = x; cx < x + w; ++cx)
                {
                    if (cx < 0 || cx >= Size) continue;

                    int at = cy * Size + cx;
                    map.Back[at] = 0;
                    map.Type[at] = Game.Cell.Empty;
                }
            }
        }

        /// <summary>
        /// Joins two rooms with a two-cell corridor.
        ///
        /// Two cells, not one: a one-cell corridor is passable but the player's sprite
        /// is wider than a cell, and the original's own maps use two for the same
        /// reason.
        /// </summary>
        private static void Connect(MapFile map, int[] a, int[] b, bool horizontal, AiArt.Rand rnd)
        {
            if (horizontal)
            {
                // Overlap of the two rooms' vertical spans, so the corridor meets both.
                int top = Math.Max(a[1], b[1]);
                int bottom = Math.Min(a[1] + a[3], b[1] + b[3]);

                if (bottom - top < 3) top = Math.Min(a[1] + a[3] / 2, b[1] + b[3] / 2) - 1;

                int y = top + rnd.Below(Math.Max(1, bottom - top - 2));

                int from = a[0] + a[2];
                int to = b[0];

                if (from > to) { int t = from; from = to; to = t; }

                Carve(map, from, y, to - from, 2);
            }
            else
            {
                int left = Math.Max(a[0], b[0]);
                int right = Math.Min(a[0] + a[2], b[0] + b[2]);

                if (right - left < 3) left = Math.Min(a[0] + a[2] / 2, b[0] + b[2] / 2) - 1;

                int x = left + rnd.Below(Math.Max(1, right - left - 2));

                int from = a[1] + a[3];
                int to = b[1];

                if (from > to) { int t = from; from = to; to = t; }

                Carve(map, x, from, 2, to - from);
            }
        }

        /// <summary>
        /// Fills part of a room with water.
        ///
        /// The cell type stays walkable and only the front layer changes: in Doom2D a
        /// river is a wall name the renderer tints, not a different kind of floor.
        /// </summary>
        private static void Flood(MapFile map, int[] room, AiArt.Rand rnd)
        {
            int depth = 2 + rnd.Below(4);
            int level = rnd.Below(3);

            string water = "_WATER_" + level;

            int slot = -1;

            for (int i = 0; i < WallNames.Length; ++i)
                if (WallNames[i] == water) { slot = i; break; }

            // The water names are not in the wall list because they have no picture:
            // they are palette remaps. A free slot holds the name.
            if (slot < 0)
            {
                slot = WallNames.Length - 1;
                map.WallName[slot] = water;
            }

            int y0 = room[1] + room[3] - depth;

            for (int cy = y0; cy < room[1] + room[3]; ++cy)
            {
                if (cy < 0 || cy >= Size) continue;

                for (int cx = room[0]; cx < room[0] + room[2]; ++cx)
                {
                    if (cx < 0 || cx >= Size) continue;

                    int at = cy * Size + cx;
                    if (map.Back[at] != 0) continue;      // only the carved floor

                    map.Front[at] = (byte)slot;
                }
            }
        }

        /// <summary>Places one thing on a free cell of a room.</summary>
        private static void AddThing(MapFile map, int[] room, short type, AiArt.Rand rnd)
        {
            for (int attempt = 0; attempt < 40; ++attempt)
            {
                int x = room[0] + 1 + rnd.Below(Math.Max(1, room[2] - 2));
                int y = room[1] + 1 + rnd.Below(Math.Max(1, room[3] - 2));

                if (x <= 0 || y <= 0 || x >= Size - 1 || y >= Size - 1) continue;

                int at = y * Size + x;
                if (map.Back[at] != 0) continue;          // must stand on the floor
                if (map.Type[at] != Game.Cell.Empty) continue;

                var thing = new MapThing();
                thing.X = (short)(x * 8 + 4);
                thing.Y = (short)(y * 8 + 4);
                thing.Type = type;
                thing.Flags = 0;

                map.Things.Add(thing);
                return;
            }
        }
    }
}
