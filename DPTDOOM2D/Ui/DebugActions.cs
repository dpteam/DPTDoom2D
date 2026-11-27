//
// DPT.DOOM 2D — the debug actions behind the F2 Debug tab.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;
using DGame = DPTDOOM2D.Game.Game;

namespace DPTDOOM2D.Ui
{
    /// <summary>
    /// What the Debug tab can do, with no UI in it.
    ///
    /// The actions live here rather than inside the overlay for one reason: they have to
    /// be testable. A debug tool that silently does nothing is worse than no tool,
    /// because it makes a developer believe the thing they are testing is fine — and a
    /// tool whose only test is "the menu opened" is exactly that. <c>--debugcheck</c>
    /// calls these and measures the effect.
    ///
    /// Every method is a plain engine call. Nothing pokes at internals: warping is
    /// <c>Game.Start</c>, spawning is <c>Monsters.Spawn</c>, and the exit is the same
    /// <c>Exit</c> flag a level's own trigger sets. A debug action that took a different
    /// path would be testing something the player never does.
    /// </summary>
    internal static class DebugActions
    {
        /// <summary>The monsters a debug spawn can place, by name.</summary>
        public static readonly string[] MonsterNames =
        {
            "imp", "demon", "zombie", "sergeant", "cyberdemon", "chaingunner",
            "baron", "knight", "cacodemon", "soul", "pain", "spider",
            "brain", "mancubus", "skeleton", "vile", "fish", "barrel", "robot", "man"
        };

        public static readonly int[] MonsterChoices =
        {
            Monsters.MnImp, Monsters.MnDemon, Monsters.MnZomby, Monsters.MnSerg,
            Monsters.MnCyber, Monsters.MnCGun, Monsters.MnBaron, Monsters.MnKnight,
            Monsters.MnCaco, Monsters.MnSoul, Monsters.MnPain, Monsters.MnSpider,
            Monsters.MnBsp, Monsters.MnMancub, Monsters.MnSkel, Monsters.MnVile,
            Monsters.MnFish, Monsters.MnBarrel, Monsters.MnRobo, Monsters.MnMan
        };

        public static readonly string[] ItemNames =
        {
            "clip", "shells", "rocket", "cell", "ammo box", "shell box",
            "rocket box", "cell pack", "stim", "medikit", "big medikit",
            "chainsaw", "shotgun", "super shotgun", "chaingun", "launcher",
            "plasma", "bfg", "soul sphere", "mega sphere", "invisibility",
            "aqua", "red key", "green key", "blue key", "suit"
        };

        public static readonly int[] ItemChoices =
        {
            ItemType.Clip, ItemType.Shel, ItemType.Rocket, ItemType.Cell,
            ItemType.Ammo, ItemType.SBox, ItemType.RBox, ItemType.CelP,
            ItemType.Stim, ItemType.Medi, ItemType.BPack,
            ItemType.CSaw, ItemType.SGun, ItemType.SGun2, ItemType.MGun,
            ItemType.Laun, ItemType.Plas, ItemType.BFG,
            ItemType.Arm1, ItemType.Arm2, ItemType.Invl,
            ItemType.Aqua, ItemType.KeyR, ItemType.KeyG, ItemType.KeyB, ItemType.Suit
        };

        /// <summary>
        /// How many levels this data set has.
        ///
        /// Counted from the WAD rather than assumed to be nineteen: a mod may ship fewer,
        /// and warping past the end is a crash.
        /// </summary>
        public static int MapCount()
        {
            int count = 0;

            for (int i = 1; i <= 99; ++i)
                if (Wad.FindRes(string.Format("MAP{0:00}", i)) >= 0) count = i;

            return count < 1 ? 1 : count;
        }

        /// <summary>Warping is only meaningful while a level is running.</summary>
        public static bool InGame
        {
            get { return DGame.State == DGame.GsGame; }
        }

        /// <summary>Starts the given level, exactly as the menu would.</summary>
        public static void Warp(int map)
        {
            int count = MapCount();

            if (map < 1) map = 1;
            if (map > count) map = count;

            DGame.Map = map;
            DGame.Start();
        }

        /// <summary>Finishes the level through the same flag the exit trigger sets.</summary>
        public static void ExitLevel()
        {
            DGame.Exit = 1;
        }

        /// <summary>Restarts the level that is already loaded.</summary>
        public static void Reload()
        {
            DGame.Start();
        }

        /// <summary>
        /// Spawns a monster a little in front of the player.
        ///
        /// In front, not at the player's feet: a spawn you cannot see is a spawn you
        /// cannot check. Returns the monster slot, or -1 when the level is full or the
        /// player is against the edge of the map.
        /// </summary>
        public static int SpawnMonster(int choice)
        {
            if (choice < 0 || choice >= MonsterChoices.Length) choice = 0;

            int x, y;
            if (!FrontOfPlayer(24, out x, out y)) return -1;

            return Monsters.Spawn(x, y, (byte)(DGame.Pl1.D == 0 ? 0 : 1), MonsterChoices[choice]);
        }

        /// <summary>Drops an item in front of the player. False when there is no room.</summary>
        public static bool SpawnItem(int choice)
        {
            if (choice < 0 || choice >= ItemChoices.Length) choice = 0;

            int x, y;
            if (!FrontOfPlayer(20, out x, out y)) return false;

            Items.Spawn(x, y, ItemChoices[choice]);
            return true;
        }

        /// <summary>Removes every monster, and returns how many went.</summary>
        public static int KillAll()
        {
            return Monsters.KillAll();
        }

        /// <summary>
        /// Gives the player everything a level can give: health, armour, every weapon,
        /// every key and full ammunition.
        ///
        /// Written as the same field assignments the item pickups make, rather than by
        /// calling the pickup code, because the pickup code plays sounds and shows
        /// messages — and a debug refill that fires six sounds is a debug refill that
        /// makes you jump.
        /// </summary>
        public static void Refill()
        {
            PlayerData p = DGame.Pl1;

            p.Life = 100;
            p.Armor = 100;

            p.Ammo = 200;
            p.Shel = 50;
            p.Rock = 50;
            p.Cell = 300;
            p.Fuel = 300;

            p.Wpns = 0xFF;      // every weapon the game has
            p.Keys = 0x07;      // red, green and blue

            if (DGame.TwoPl)
            {
                PlayerData q = DGame.Pl2;

                q.Life = 100;
                q.Armor = 100;
                q.Ammo = 200;
                q.Shel = 50;
                q.Rock = 50;
                q.Cell = 300;
                q.Fuel = 300;
                q.Wpns = 0xFF;
                q.Keys = 0x07;
            }
        }

        /// <summary>
        /// A point a few pixels in front of the player, clamped into the level.
        ///
        /// "In front" is the direction he faces, which is what makes a spawned monster
        /// visible instead of behind the camera. The bounds check keeps a spawn at the
        /// edge of the map inside it rather than wrapping to the other side.
        /// </summary>
        private static bool FrontOfPlayer(int distance, out int x, out int y)
        {
            PlayerData p = DGame.Pl1;

            int dx = p.D == 0 ? distance : -distance;

            x = p.O.X + dx;
            y = p.O.Y - 8;

            if (x < 16 || y < 16) return false;
            if (x >= MapFile.Width * 8 - 16 || y >= MapFile.Height * 8 - 16) return false;

            return true;
        }
    }
}
