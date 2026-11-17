//
// DPT.DOOM 2D — the actor registry: built-in monster data plus actors.txt.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DPTDOOM2D.Game;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Monster data as data: sizes, sprite sets, sounds and state frame sequences.
    ///
    /// The twenty built-in monsters are defined below in the same language a mod
    /// uses, so the parser is exercised by every launch and there is exactly one
    /// way to describe a monster. A mod ships actors.txt and overrides any of
    /// them by name; the AI itself stays in monster.c, which is why a definition
    /// can only supply data.
    ///
    /// New monster types are not supported yet: the type is an index into the
    /// tables, and a save file stores it as a byte with a fixed meaning. See
    /// docs/ROADMAP.md, section 1.
    /// </summary>
    public static class Actors
    {
        public const int TypeCount = 20;

        public const int LabelCount = 10;

        // The order matters: the first nine are the save file's numbering
        // (monster.c's AnimIndex: spawn, see, pain, wait, melee, death, slop, dead,
        // mess), so an old save still maps to the right animation. Missile has no
        // slot there and keeps its own code.
        public const int LblSpawn = 0, LblSee = 1, LblPain = 2, LblWait = 3, LblMelee = 4,
                         LblDeath = 5, LblSlop = 6, LblDead = 7, LblMess = 8, LblMissile = 9;

        public const int SoundCount = 5;
        public const int SndSee = 0, SndPain = 1, SndAttack = 2, SndIdle = 3, SndDeath = 4;

        /// <summary>Monster sizes: monster.c's mnsz table.</summary>
        public sealed class Size
        {
            public int R;    // radius
            public int H;    // height
            public int L;    // hit points
            public int Mp;   // pain threshold
            public int Rv;   // walk speed
            public int Jv;   // jump strength
            public int Sp;   // gib limit (life below -Sp leaves a mess)
            public int MinP; // pain floor
        }

        /// <summary>Built-in actors, in the engine's type order: index + 1 is the type.</summary>
        public static readonly string[] Names =
        {
            "Demon", "Imp", "Zombie", "Sergeant", "Cyberdemon", "Chaingunner", "Baron",
            "Knight", "Cacodemon", "Soul", "PainElemental", "Spider", "Arachnotron",
            "Mancubus", "Revenant", "Archvile", "Fish", "Barrel", "Robot", "Man"
        };

        /// <summary>Index 0 is unused, so a type can index this directly like monster.c does.</summary>
        public static readonly Size[] Sizes = CreateSizes();

        static Size[] CreateSizes()
        {
            // Allocated up front so that reading the table before Load() gives
            // zeros instead of a NullReferenceException.
            var table = new Size[TypeCount + 1];
            for (int i = 0; i <= TypeCount; ++i) table[i] = new Size();
            return table;
        }

        public static readonly string[] Sprite = new string[TypeCount];
        public static readonly int[] Frames = new int[TypeCount];
        public static readonly string[][] Sound = new string[TypeCount][];
        public static readonly string[][] Anim = new string[TypeCount][];

        /// <summary>Render style: see <see cref="StyleNormal"/>, <see cref="StyleInvisible"/>, <see cref="StyleFuzz"/>.</summary>
        public const int StyleNormal = 0, StyleInvisible = 1, StyleFuzz = 2;

        public static readonly int[] Style = new int[TypeCount];

        /// <summary>
        /// Palette translation: the base colour the sprite's 0x70..0x7F range is
        /// remapped to, 0 for none. It is the same trick the blitter already does
        /// for player colours, which is why the corpse is 0xD0.
        /// </summary>
        public static readonly int[] Translation = new int[TypeCount];

        /// <summary>True when the actor is meant to be invisible: sprite "none", "NULL", "----" or "TNT1".</summary>
        public static readonly bool[] Hidden = new bool[TypeCount];

        /// <summary>
        /// A digest of the compiled tables, for the network manifest: two machines
        /// that disagree about any size, sprite, sound or frame sequence would
        /// desync, and this is the one number that covers all of them.
        /// </summary>
        public static uint Digest()
        {
            Load();

            uint hash = 2166136261u;

            for (int i = 0; i < TypeCount; ++i)
            {
                Size size = Sizes[i + 1];
                hash = Mix(hash, (uint)size.R);
                hash = Mix(hash, (uint)size.H);
                hash = Mix(hash, (uint)size.L);
                hash = Mix(hash, (uint)size.Mp);
                hash = Mix(hash, (uint)size.Rv);
                hash = Mix(hash, (uint)size.Jv);
                hash = Mix(hash, (uint)size.Sp);
                hash = Mix(hash, (uint)size.MinP);

                hash = MixText(hash, Sprite[i]);
                hash = Mix(hash, (uint)Frames[i]);
                hash = Mix(hash, (uint)Style[i]);
                hash = Mix(hash, (uint)Translation[i]);

                for (int s = 0; s < SoundCount; ++s) hash = MixText(hash, Sound[i][s]);
                for (int l = 0; l < LabelCount; ++l) hash = MixText(hash, Anim[i][l]);
            }

            return hash;
        }

        static uint MixText(uint hash, string text)
        {
            if (text == null) return Mix(hash, 0);

            hash = Mix(hash, (uint)text.Length);
            for (int i = 0; i < text.Length; ++i)
                hash = Mix(hash, text[i]);

            return hash;
        }

        /// <summary>
        /// FNV-1a, the same mixer the tick checksum uses. It lives here rather than in
        /// NetProtocol so the actor tables can be digested by a tool that has no
        /// network code compiled in — the editor, for one.
        /// </summary>
        static uint Mix(uint hash, uint value)
        {
            hash ^= value & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 8) & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 16) & 0xFF;
            hash *= 16777619u;
            hash ^= (value >> 24) & 0xFF;
            hash *= 16777619u;
            return hash;
        }

        /// <summary>Parse problems, already formatted as file:line: code: message.</summary>
        public static readonly List<string> Errors = new List<string>();

        /// <summary>Where the data came from, in load order: for the log and --actorcheck.</summary>
        public static readonly List<string> Loaded = new List<string>();

        static bool loaded;

        /// <summary>
        /// Reads the built-in definitions and then every actors.txt it can find:
        /// next to the exe, in Data, and inside the loaded WADs (a PK3 ships it as
        /// a loose file, a WAD as the ACTORDEF lump). Later files win, so a mod
        /// loaded with -file overrides a map pack.
        /// </summary>
        public static void Load()
        {
            if (loaded) return;
            loaded = true;

            for (int i = 0; i <= TypeCount; ++i) Sizes[i] = new Size();
            for (int i = 0; i < TypeCount; ++i)
            {
                Sprite[i] = "";
                Sound[i] = new string[SoundCount];
                Anim[i] = new string[LabelCount];
                for (int l = 0; l < LabelCount; ++l) Anim[i][l] = "";
            }
            var errors = new List<string>();
            List<ActorDef> defs = ActorParser.Parse(Builtin, "built-in", errors);
            Loaded.Add("built-in");

            foreach (string path in DiskSources())
            {
                try
                {
                    string text = File.ReadAllText(path, Encoding.GetEncoding(1251));
                    defs.AddRange(ActorParser.Parse(text, path, errors));
                    Loaded.Add(path);
                }
                catch (Exception e)
                {
                    errors.Add(path + ":0: E110: cannot read: " + e.Message);
                }
            }

            foreach (string lump in new[] { "ACTORDEF", "actors.txt" })
            {
                int res = Wad.FindRes(lump);
                if (res < 0) continue;

                try
                {
                    byte[] data = Wad.LoadRes(res);
                    string text = Encoding.GetEncoding(1251).GetString(data);
                    defs.AddRange(ActorParser.Parse(text, "wad:" + lump, errors));
                    Loaded.Add("wad:" + lump);
                }
                catch (Exception e)
                {
                    errors.Add("wad:" + lump + ":0: E110: cannot read: " + e.Message);
                }
            }

            Build(defs, errors);

            Errors.AddRange(errors);
            for (int i = 0; i < errors.Count; ++i) Log.Warn("actors: " + errors[i]);

            Log.Info("actors: " + defs.Count + " definition(s) from " + string.Join(", ", Loaded.ToArray()));
        }

        /// <summary>actors.txt next to the exe, then in Data. Both are optional.</summary>
        static List<string> DiskSources()
        {
            var found = new List<string>();
            string dir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates = { Path.Combine(dir, "actors.txt"), Path.Combine(dir, "Data", "actors.txt") };
            for (int i = 0; i < candidates.Length; ++i)
                if (File.Exists(candidates[i])) found.Add(candidates[i]);

            return found;
        }

        /// <summary>Folds the definitions into the tables. Later ones win, field by field.</summary>
        static void Build(List<ActorDef> defs, List<string> errors)
        {
            for (int i = 0; i < defs.Count; ++i)
            {
                ActorDef def = defs[i];
                int index = IndexOf(def.Name);

                if (index == 0)
                {
                    errors.Add(def.Where + ": E108: unknown actor '" + def.Name
                        + "'; new monster types are not supported yet (see docs/ROADMAP.md)");
                    continue;
                }

                Apply(def, index - 1, errors);
            }

            for (int i = 0; i < TypeCount; ++i)
            {
                string name = Names[i];

                if (Sprite[i].Length == 0 && !Hidden[i])
                {
                    errors.Add("built-in:" + name + ": E107: no sprite");
                    Sprite[i] = "POSS";
                }

                if (Anim[i][LblSpawn].Length == 0)
                    errors.Add("built-in:" + name + ": E107: no spawn state");

                // A monster with no missile state uses its melee animation, which is
                // what the switch in SetSt did for every type but two.
                if (Anim[i][LblMissile].Length == 0)
                    Anim[i][LblMissile] = Anim[i][LblMelee];

                if (Frames[i] <= 0)
                    Frames[i] = DeriveFrames(i);

                if (Frames[i] > 58)
                {
                    errors.Add("built-in:" + name + ": E109: " + Frames[i] + " frames, the sprite table holds 58");
                    Frames[i] = 58;
                }
            }
        }

        static void Apply(ActorDef def, int i, List<string> errors)
        {
            Size size = Sizes[i + 1];

            size.R = Number(def, "radius", size.R, 0, 512, errors);
            size.H = Number(def, "height", size.H, 0, 512, errors);
            size.L = Number(def, "health", size.L, 0, 9999, errors);
            size.Mp = Number(def, "painchance", size.Mp, 0, 9999, errors);
            size.Rv = Number(def, "revisit", size.Rv, 0, 64, errors);
            size.Jv = Number(def, "jump", size.Jv, 0, 64, errors);
            size.Sp = Number(def, "slop", size.Sp, 0, 999, errors);
            size.MinP = Number(def, "minpain", size.MinP, 0, 999, errors);

            // An override only touches the fields it names: "actor Imp { health 99 }"
            // must keep the imp's sprite, sounds and states.
            if (def.SpriteSet)
            {
                if (def.Sprite.Length == 0)
                    Hidden[i] = true;
                else
                {
                    Sprite[i] = def.Sprite;
                    Hidden[i] = false;
                }
            }

            int style;
            if (def.Props.TryGetValue("renderstyle", out style)) Style[i] = style;

            int translation;
            if (def.Props.TryGetValue("translation", out translation))
            {
                if (translation < 0 || translation > 255)
                {
                    errors.Add(def.Where + ": E103: 'translation' must be a palette index 0..255, got " + translation);
                }
                else
                {
                    Translation[i] = translation;
                }
            }

            int frames;
            if (def.Props.TryGetValue("frames", out frames)) Frames[i] = frames;

            for (int s = 0; s < SoundCount; ++s)
            {
                string lump;
                if (def.Sounds.TryGetValue(ActorParser.SoundSlots[s], out lump))
                    Sound[i][s] = lump;
            }

            for (int l = 0; l < LabelCount; ++l)
            {
                string frames2;
                if (def.States.TryGetValue(ActorParser.Labels[l], out frames2))
                    Anim[i][l] = frames2;
            }
        }

        static int Number(ActorDef def, string key, int fallback, int min, int max, List<string> errors)
        {
            int value;
            if (!def.Props.TryGetValue(key, out value)) return fallback;

            if (value < min || value > max)
            {
                errors.Add(def.Where + ": E103: '" + key + "' must be " + min + ".." + max + ", got " + value);
                return fallback;
            }

            return value;
        }

        /// <summary>
        /// How many sprite frames to load: enough for the highest frame letter any
        /// state uses, two rotations each. The built-ins state it explicitly
        /// instead, because monster.c deliberately loads fewer than the death
        /// animation names for one of them (the lost soul) and the original
        /// picture depends on that.
        /// </summary>
        static int DeriveFrames(int i)
        {
            int max = 0;

            for (int l = 0; l < LabelCount; ++l)
            {
                string frames = Anim[i][l];
                for (int c = 0; c < frames.Length; ++c)
                {
                    int at = frames[c] - 'A';
                    if (at > max) max = at;
                }
            }

            return (max + 1) * 2;
        }

        // ------------------------------------------------------------------ lookup

        /// <summary>Frame string of one state, or "" when the state is unused.</summary>
        public static string AnimOf(int type, int label)
        {
            Load();
            if (type < 1 || type > TypeCount) return "";
            if (label < 0 || label >= LabelCount) return "";

            return Anim[type - 1][label];
        }

        /// <summary>Built-in type for a name, 0 when there is none.</summary>
        public static int IndexOf(string name)
        {
            for (int i = 0; i < Names.Length; ++i)
                if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase)) return i + 1;
            return 0;
        }

        public static string NameOf(int type)
        {
            if (type < 1 || type > TypeCount) return "?";
            return Names[type - 1];
        }

        // ------------------------------------------------------------------ data

        /// <summary>
        /// The 1996 tables, written in the language a mod uses.
        ///
        /// Every number and frame string here comes from monster.c: mnsz, mms,
        /// msn, sn and the nine animation arrays. --actorcheck compares the parsed
        /// result with a frozen copy of those arrays, so a typo in this block is a
        /// failed check rather than a monster with the wrong hit points.
        /// </summary>
        const string Builtin = @"
// ---------------------------------------------------------------- 1 demon
actor Demon
{
    radius 15   height 28   health 60   painchance 20
    revisit 7   jump 10     slop 0      minpain 10
    sprite SARG    frames 28
    sound see    DMACT
    sound pain   DMPAIN
    sound attack SGTATK
    sound idle   SGTSIT
    sound death  SGTDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDD
    pain:   H
    wait:   A
    melee:  EEFFGG
    death:  IIIJJJKKKLLLMMM
    dead:   N
    }
}

// ---------------------------------------------------------------- 2 imp
actor Imp
{
    radius 10   height 28   health 25   painchance 15
    revisit 3   jump 10     slop 30     minpain 0
    sprite TROO    frames 42
    sound see    BGACT
    sound pain   POPAIN
    sound attack CLAW
    states
    {
    spawn:  AAABBB
    see:    AABBCCDD
    pain:   H
    wait:   A
    melee:  EEFFGG
    death:  IIIJJJKKKLLL
    slop:   NNNOOOPPPRRRSSSTTT
    dead:   M
    mess:   U
    }
}

// ---------------------------------------------------------------- 3 zombieman
actor Zombie
{
    radius 10   height 28   health 15   painchance 10
    revisit 3   jump 10     slop 30     minpain 0
    sprite POSS    frames 42
    sound see    POSACT
    sound pain   POPAIN
    states
    {
    spawn:  AAABBB
    see:    AABBDDAACCDD
    pain:   G
    wait:   A
    melee:  EEEEEF
    death:  HHHIIIJJJKKK
    slop:   MMMNNNOOOPPPRRRSSSTTT
    dead:   L
    mess:   U
    }
}

// ---------------------------------------------------------------- 4 shotgun guy
actor Sergeant
{
    radius 10   height 28   health 20   painchance 10
    revisit 3   jump 10     slop 30     minpain 0
    sprite SPOS    frames 42
    sound see    POSACT
    sound pain   POPAIN
    states
    {
    spawn:  AAABBB
    see:    AABBDDAACCDD
    pain:   G
    wait:   A
    melee:  EEEEEF
    death:  HHHIIIJJJKKK
    slop:   MMMNNNOOOPPPRRRSSSTTT
    dead:   L
    mess:   U
    }
}

// ---------------------------------------------------------------- 5 cyberdemon
actor Cyberdemon
{
    radius 20   height 55   health 500   painchance 70
    revisit 5   jump 10     slop 0       minpain 50
    sprite CYBR    frames 32
    sound pain   DMPAIN
    sound attack HOOF
    sound idle   CYBSIT
    sound death  CYBDTH
    states
    {
    spawn:  AAABBB
    see:    AABBDDCCDDBB
    pain:   G
    wait:   A
    melee:  EEEEEF
    death:  HHHIIIJJJKKKLLLMMMNNNOOO
    dead:   P
    }
}

// ---------------------------------------------------------------- 6 chaingunner
actor Chaingunner
{
    radius 12   height 28   health 60   painchance 20
    revisit 3   jump 10     slop 30     minpain 10
    sprite CPOS    frames 40
    sound see    POSACT
    sound pain   POPAIN
    states
    {
    spawn:  AAABBB
    see:    AABBDDAACCDD
    pain:   G
    wait:   A
    melee:  EF
    death:  HHHIIIJJJKKKLLLMMM
    slop:   OOOPPPQQQRRRSSS
    dead:   N
    mess:   T
    }
}

// ---------------------------------------------------------------- 7 baron of hell
actor Baron
{
    radius 12   height 32   health 150   painchance 40
    revisit 3   jump 10     slop 0       minpain 30
    sprite BOSS    frames 30
    sound pain   DMPAIN
    sound idle   BRSSIT
    sound death  BRSDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDD
    pain:   H
    wait:   A
    melee:  EEFFGG
    death:  IIIJJJKKKLLLMMMNNN
    dead:   O
    }
}

// ---------------------------------------------------------------- 8 hell knight
actor Knight
{
    radius 12   height 32   health 75   painchance 40
    revisit 3   jump 10     slop 0      minpain 30
    sprite BOS2    frames 30
    sound pain   DMPAIN
    sound idle   KNTSIT
    sound death  KNTDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDD
    pain:   H
    wait:   A
    melee:  EEFFGG
    death:  IIIJJJKKKLLLMMMNNN
    dead:   O
    }
}

// ---------------------------------------------------------------- 9 cacodemon
actor Cacodemon
{
    radius 15   height 28   health 100   painchance 10
    revisit 4   jump 4      slop 0      minpain 0
    sprite HEAD    frames 24
    sound see    DMACT
    sound pain   DMPAIN
    sound idle   CACSIT
    sound death  CACDTH
    states
    {
    spawn:  A
    see:    A
    pain:   F
    wait:   A
    melee:  BBCCDD
    death:  GGGHHHIIIJJJKKK
    dead:   L
    }
}

// ---------------------------------------------------------------- 10 lost soul
actor Soul
{
    radius 8    height 18   health 60   painchance 10
    revisit 4   jump 4      slop 0      minpain 0
    sprite SKUL    frames 22
    sound see    DMACT
    sound pain   DMPAIN
    sound attack SKLATK
    sound idle   SKLATK
    sound death  FIRXPL
    states
    {
    spawn:  AAABBB
    see:    AABB
    pain:   E
    wait:   AABB
    melee:  CCDD
    death:  FFFGGGHHHIIIJJJKKK
    dead:   none
    }
}

// ---------------------------------------------------------------- 11 pain elemental
actor PainElemental
{
    radius 15   height 28   health 100   painchance 10
    revisit 4   jump 4      slop 0       minpain 0
    sprite PAIN    frames 26
    sound see    DMACT
    sound pain   PEPAIN
    sound idle   PESIT
    sound death  PEDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCBB
    pain:   G
    wait:   A
    melee:  DDEEFF
    death:  HHHIIIJJJKKKLLLMMM
    dead:   none
    }
}

// ---------------------------------------------------------------- 12 spider mastermind
actor Spider
{
    radius 64   height 50   health 500   painchance 70
    revisit 4   jump 10     slop 0       minpain 50
    sprite SPID    frames 38
    sound pain   DMPAIN
    sound attack METAL
    sound idle   SPISIT
    sound death  SPIDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDDEEFF
    pain:   I
    wait:   A
    melee:  GH
    death:  JJJJKKKKLLLLMMMMNNNNOOOOPPPPQQQQRRRR
    dead:   S
    }
}

// ---------------------------------------------------------------- 13 arachnotron
actor Arachnotron
{
    radius 25   height 27   health 150   painchance 20
    revisit 4   jump 10     slop 0       minpain 0
    sprite BSPI    frames 32
    sound see    BSPACT
    sound pain   DMPAIN
    sound attack BSPWLK
    sound idle   BSPSIT
    sound death  BSPDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDDEEFF
    pain:   I
    wait:   A
    melee:  GH
    death:  JJJKKKLLLMMMNNNOOO
    dead:   P
    }
}

// ---------------------------------------------------------------- 14 mancubus
actor Mancubus
{
    radius 18   height 30   health 200   painchance 40
    revisit 3   jump 7      slop 0       minpain 20
    sprite FATT    frames 40
    sound see    DMACT
    sound pain   MNPAIN
    sound attack MANATK
    sound idle   MANSIT
    sound death  MANDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDDEEFF
    pain:   J
    wait:   I
    melee:  GGGGHH
    death:  KKKLLLMMMNNNOOOPPPRRRSSS
    dead:   T
    }
}

// ---------------------------------------------------------------- 15 revenant
actor Revenant
{
    radius 17   height 36   health 200   painchance 40
    revisit 6   jump 11     slop 0       minpain 20
    sprite SKEL    frames 34
    sound see    SKEACT
    sound pain   POPAIN
    sound attack SKEATK
    sound idle   SKESIT
    sound death  SKEDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDDEEFF
    pain:   L
    wait:   K
    melee:  GGHHII
    missile: KKKKJJ
    death:  MMMNNNOOOPPP
    dead:   Q
    }
}

// ---------------------------------------------------------------- 16 archvile
actor Archvile
{
    radius 17   height 36   health 150   painchance 30
    revisit 7   jump 12     slop 0       minpain 10
    sprite VILE    frames 58
    sound see    VILACT
    sound pain   VIPAIN
    sound attack VILATK
    sound idle   VILSIT
    sound death  VILDTH
    states
    {
    spawn:  AAABBB
    see:    AABBCCDDEEFF
    pain:   Q
    wait:   A
    melee:  ""[[\\\\]]""
    death:  RRRSSSTTTUUUVVVWWWXXXYYY
    dead:   Z
    }
}

// ---------------------------------------------------------------- 17 fish
actor Fish
{
    radius 5    height 5    health 35   painchance 20
    revisit 14  jump 6      slop 0      minpain 10
    sprite FISH    frames 12
    sound attack BITE1
    states
    {
    spawn:  AAABBB
    see:    AABB
    pain:   EECCDDCC
    wait:   A
    melee:  BBFFAA
    death:  DDDD
    dead:   C
    }
}

// ---------------------------------------------------------------- 18 barrel
actor Barrel
{
    radius 5    height 17   health 20   painchance 0
    revisit 7   jump 6      slop 0      minpain 0
    sprite BAR1    frames 4
    sound death  BAREXP
    states
    {
    spawn:  A
    see:    A
    pain:   A
    wait:   A
    melee:  A
    death:  CCCDDDEEEFFFGGG
    dead:   none
    }
}

// ---------------------------------------------------------------- 19 robot
actor Robot
{
    radius 17   height 38   health 20   painchance 40
    revisit 3   jump 6      slop 0      minpain 20
    sprite ROBO    frames 34
    sound see    BSPACT
    sound attack BSPWLK
    sound idle   BSPSIT
    sound death  BSPDTH
    states
    {
    spawn:  A
    see:    DDEEFFGGHHIIJJKKLLAABBCC
    pain:   D
    wait:   D
    melee:  OOPPQQ
    missile: MN
    death:  D
    dead:   D
    }
}

// ---------------------------------------------------------------- 20 player corpse
actor Man
{
    radius 8    height 26   health 400   painchance 70
    revisit 8   jump 10     slop 30     minpain 50
    sprite PLAY    frames 46
    translation 0xD0
    sound see    HAHA1
    sound pain   PLPAIN
    sound idle   STOP1
    sound death  PDIEHI
    states
    {
    spawn:  AAABBB
    see:    ACDABD
    pain:   G
    wait:   E
    melee:  EEEEFF
    death:  HHHHIIIIJJJJKKKKLLLLMMMM
    slop:   OOPPQQRRSSTTUUVV
    dead:   N
    mess:   W
    }
}
";
    }
}
