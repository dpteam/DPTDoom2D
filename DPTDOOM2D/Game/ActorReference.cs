//
// DPT.DOOM 2D — the 1996 monster tables, frozen for --actorcheck.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Game
{
    /// <summary>
    /// The tables monster.c used to keep, transcribed from
    /// refs/doom2d-rembo/src/monster.c and frozen here.
    ///
    /// Nothing at runtime reads this class: the engine takes its data from
    /// <see cref="Actors"/>, which a mod can override. The point of the copy is
    /// --actorcheck, which compares the parsed data with these arrays. Without it
    /// a typo in the built-in block would quietly give the cyberdemon 500 hit
    /// points and 5000, and nothing would say a word.
    ///
    /// The animation strings are the *effective* ones: the archvile's melee is the
    /// override SetSt applied ("[[\\]]"), not the attackAnim entry it replaced.
    /// </summary>
    internal static class ActorReference
    {
        /// <summary>radius, height, life, painchance, revisit, jump, slop, minpain. Row 0 is unused.</summary>
        public static readonly int[][] Sizes =
        {
            new[] {  0,  0,    0,    0,  0,  0,    0,    0 },
            new[] { 15, 28,   60,   20,  7, 10,    0,   10 },
            new[] { 10, 28,   25,   15,  3, 10,   30,    0 },
            new[] { 10, 28,   15,   10,  3, 10,   30,    0 },
            new[] { 10, 28,   20,   10,  3, 10,   30,    0 },
            new[] { 20, 55,  500,   70,  5, 10,    0,   50 },
            new[] { 12, 28,   60,   20,  3, 10,   30,   10 },
            new[] { 12, 32,  150,   40,  3, 10,    0,   30 },
            new[] { 12, 32,   75,   40,  3, 10,    0,   30 },
            new[] { 15, 28,  100,   10,  4,  4,    0,    0 },
            new[] {  8, 18,   60,   10,  4,  4,    0,    0 },
            new[] { 15, 28,  100,   10,  4,  4,    0,    0 },
            new[] { 64, 50,  500,   70,  4, 10,    0,   50 },
            new[] { 25, 27,  150,   20,  4, 10,    0,    0 },
            new[] { 18, 30,  200,   40,  3,  7,    0,   20 },
            new[] { 17, 36,  200,   40,  6, 11,    0,   20 },
            new[] { 17, 36,  150,   30,  7, 12,    0,   10 },
            new[] {  5,  5,   35,   20, 14,  6,    0,   10 },
            new[] {  5, 17,   20,    0,  7,  6,    0,    0 },
            new[] { 17, 38,   20,   40,  3,  6,    0,   20 },
            new[] {  8, 26,  400,   70,  8, 10,   30,   50 }
        };

        public static readonly string[] Sprites =
        {
            "SARG", "TROO", "POSS", "SPOS", "CYBR", "CPOS", "BOSS", "BOS2", "HEAD", "SKUL",
            "PAIN", "SPID", "BSPI", "FATT", "SKEL", "VILE", "FISH", "BAR1", "ROBO", "PLAY"
        };

        public static readonly int[] Frames =
        {
            14 * 2, 21 * 2, 21 * 2, 21 * 2, 16 * 2, 20 * 2, 15 * 2, 15 * 2, 12 * 2, 11 * 2,
            13 * 2, 19 * 2, 16 * 2, 20 * 2, 17 * 2, 29 * 2, 6 * 2, 2 * 2, 17 * 2, 23 * 2
        };

        /// <summary>see, pain, attack, idle, death. "" is a silent slot.</summary>
        public static readonly string[][] Sounds =
        {
            new[] { "DMACT", "DMPAIN", "SGTATK", "SGTSIT", "SGTDTH" },
            new[] { "BGACT", "POPAIN", "CLAW", "", "" },
            new[] { "POSACT", "POPAIN", "", "", "" },
            new[] { "POSACT", "POPAIN", "", "", "" },
            new[] { "", "DMPAIN", "HOOF", "CYBSIT", "CYBDTH" },
            new[] { "POSACT", "POPAIN", "", "", "" },
            new[] { "", "DMPAIN", "", "BRSSIT", "BRSDTH" },
            new[] { "", "DMPAIN", "", "KNTSIT", "KNTDTH" },
            new[] { "DMACT", "DMPAIN", "", "CACSIT", "CACDTH" },
            new[] { "DMACT", "DMPAIN", "SKLATK", "SKLATK", "FIRXPL" },
            new[] { "DMACT", "PEPAIN", "", "PESIT", "PEDTH" },
            new[] { "", "DMPAIN", "METAL", "SPISIT", "SPIDTH" },
            new[] { "BSPACT", "DMPAIN", "BSPWLK", "BSPSIT", "BSPDTH" },
            new[] { "DMACT", "MNPAIN", "MANATK", "MANSIT", "MANDTH" },
            new[] { "SKEACT", "POPAIN", "SKEATK", "SKESIT", "SKEDTH" },
            new[] { "VILACT", "VIPAIN", "VILATK", "VILSIT", "VILDTH" },
            new[] { "", "", "BITE1", "", "" },
            new[] { "", "", "", "", "BAREXP" },
            new[] { "BSPACT", "", "BSPWLK", "BSPSIT", "BSPDTH" },
            new[] { "HAHA1", "PLPAIN", "", "STOP1", "PDIEHI" }
        };

        /// <summary>The nine saved states, in the order the save file numbers them.</summary>
        public static readonly string[][] Anim =
        {
            // spawn
            new[] { "AAABBB", "AAABBB", "AAABBB", "AAABBB", "AAABBB", "AAABBB", "AAABBB", "AAABBB",
                    "A", "AAABBB", "AAABBB", "AAABBB", "AAABBB", "AAABBB", "AAABBB", "AAABBB",
                    "AAABBB", "A", "A", "AAABBB" },
            // see
            new[] { "AABBCCDD", "AABBCCDD", "AABBDDAACCDD", "AABBDDAACCDD", "AABBDDCCDDBB",
                    "AABBDDAACCDD", "AABBCCDD", "AABBCCDD", "A", "AABB", "AABBCCBB",
                    "AABBCCDDEEFF", "AABBCCDDEEFF", "AABBCCDDEEFF", "AABBCCDDEEFF", "AABBCCDDEEFF",
                    "AABB", "A", "DDEEFFGGHHIIJJKKLLAABBCC", "ACDABD" },
            // pain
            new[] { "H", "H", "G", "G", "G", "G", "H", "H", "F", "E", "G", "I", "I", "J", "L", "Q",
                    "EECCDDCC", "A", "D", "G" },
            // wait
            new[] { "A", "A", "A", "A", "A", "A", "A", "A", "A", "AABB", "A", "A", "A", "I", "K", "A",
                    "A", "A", "D", "E" },
            // melee
            new[] { "EEFFGG", "EEFFGG", "EEEEEF", "EEEEEF", "EEEEEF", "EF", "EEFFGG", "EEFFGG",
                    "BBCCDD", "CCDD", "DDEEFF", "GH", "GH", "GGGGHH", "GGHHII",
                    "[[\\\\]]", "BBFFAA", "A", "OOPPQQ", "EEEEFF" },
            // death
            new[] { "IIIJJJKKKLLLMMM", "IIIJJJKKKLLL", "HHHIIIJJJKKK", "HHHIIIJJJKKK",
                    "HHHIIIJJJKKKLLLMMMNNNOOO", "HHHIIIJJJKKKLLLMMM",
                    "IIIJJJKKKLLLMMMNNN", "IIIJJJKKKLLLMMMNNN", "GGGHHHIIIJJJKKK",
                    "FFFGGGHHHIIIJJJKKK", "HHHIIIJJJKKKLLLMMM",
                    "JJJJKKKKLLLLMMMMNNNNOOOOPPPPQQQQRRRR", "JJJKKKLLLMMMNNNOOO",
                    "KKKLLLMMMNNNOOOPPPRRRSSS", "MMMNNNOOOPPP", "RRRSSSTTTUUUVVVWWWXXXYYY",
                    "DDDD", "CCCDDDEEEFFFGGG", "D", "HHHHIIIIJJJJKKKKLLLLMMMM" },
            // slop
            new[] { "", "NNNOOOPPPRRRSSSTTT", "MMMNNNOOOPPPRRRSSSTTT", "MMMNNNOOOPPPRRRSSSTTT", "",
                    "OOOPPPQQQRRRSSS", "", "", "", "", "", "", "", "", "", "", "", "", "",
                    "OOPPQQRRSSTTUUVV" },
            // dead
            new[] { "N", "M", "L", "L", "P", "N", "O", "O", "L", "", "", "S", "P", "T", "Q", "Z", "C",
                    "", "D", "N" },
            // mess
            new[] { "", "U", "U", "U", "", "T", "", "", "", "", "", "", "", "", "", "", "", "", "",
                    "W" }
        };

        /// <summary>Missile states that are not one of the nine saved animations.</summary>
        public static readonly int[] MissileType = { 15, 19 };      // revenant, robot
        public static readonly string[] MissileAnim = { "KKKKJJ", "MN" };

        /// <summary>
        /// Render styles and palette translations. Every built-in monster draws
        /// normally except the player corpse, which monster.c recoloured with
        /// ManColor (0xD0).
        /// </summary>
        public static readonly int[] Styles = new int[20];

        public static readonly int[] Translations =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xD0
        };

        /// <summary>
        /// Compares the live tables with the frozen ones. Returns the number of
        /// differences and describes each one.
        /// </summary>
        public static int Compare(List<string> problems)
        {
            int before = problems.Count;

            for (int type = 1; type <= Actors.TypeCount; ++type)
            {
                string name = Actors.NameOf(type);
                int row = type - 1;

                SizeCheck(problems, name, type);
                Diff(problems, name + " sprite", Sprites[row], Actors.Sprite[row]);
                Number(problems, name + " frames", Frames[row], Actors.Frames[row]);
                Number(problems, name + " renderstyle", Styles[row], Actors.Style[row]);
                Number(problems, name + " translation", Translations[row], Actors.Translation[row]);
                Number(problems, name + " hidden", 0, Actors.Hidden[row] ? 1 : 0);

                for (int s = 0; s < Actors.SoundCount; ++s)
                    Diff(problems, name + " sound " + s, Sounds[row][s], Actors.Sound[row][s] ?? "");

                for (int l = 0; l < 9; ++l)
                    Diff(problems, name + " " + ActorParserLabels[l], Anim[l][row], Actors.Anim[row][l]);
            }

            for (int i = 0; i < MissileType.Length; ++i)
                Diff(problems, Actors.NameOf(MissileType[i]) + " missile",
                    MissileAnim[i], Actors.Anim[MissileType[i] - 1][Actors.LblMissile]);

            return problems.Count - before;
        }

        static readonly string[] ActorParserLabels =
        {
            "spawn", "see", "pain", "wait", "melee", "death", "slop", "dead", "mess"
        };

        static void SizeCheck(List<string> problems, string name, int type)
        {
            Actors.Size size = Actors.Sizes[type];
            int[] row = Sizes[type];

            Number(problems, name + " radius", row[0], size.R);
            Number(problems, name + " height", row[1], size.H);
            Number(problems, name + " health", row[2], size.L);
            Number(problems, name + " painchance", row[3], size.Mp);
            Number(problems, name + " revisit", row[4], size.Rv);
            Number(problems, name + " jump", row[5], size.Jv);
            Number(problems, name + " slop", row[6], size.Sp);
            Number(problems, name + " minpain", row[7], size.MinP);
        }

        static void Number(List<string> problems, string what, int expected, int actual)
        {
            if (expected == actual) return;
            problems.Add(what + ": expected " + expected + ", got " + actual);
        }

        static void Diff(List<string> problems, string what, string expected, string actual)
        {
            if (string.Equals(expected, actual, StringComparison.Ordinal)) return;
            problems.Add(what + ": expected \"" + expected + "\", got \"" + actual + "\"");
        }
    }
}
