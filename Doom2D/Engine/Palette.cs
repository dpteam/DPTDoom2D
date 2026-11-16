using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace Doom2D.Engine
{
    /// <summary>
    /// main.c / vga.c — the palette and the lookup tables built from it.
    ///
    /// PLAYPAL holds 6-bit RGB (0..63); SDL scaled it by 4, so we do too.
    /// </summary>
    public static class Palette
    {
        /// <summary>main_pal: PLAYPAL straight from the WAD (0..63 per channel).</summary>
        public static readonly byte[] Main = new byte[768];

        /// <summary>std_pal: main_pal after the brightness curve.</summary>
        public static readonly byte[] Std = new byte[768];

        /// <summary>bright[]: perceived brightness per palette index, used by smoke.</summary>
        public static readonly byte[] Bright = new byte[256];

        /// <summary>clrmap: 12 colormaps of 256 entries (distance/light shading).</summary>
        public static readonly byte[] ClrMap = new byte[256 * 12];

        /// <summary>mixmap[256][256]: smoke dissolve table.</summary>
        public static readonly byte[] MixMap = new byte[256 * 256];

        /// <summary>RGBA for the framebuffer blit.</summary>
        public static readonly Color[] Colors = new Color[256];

        /// <summary>gamcor[5][64] from gamma.dat — brightness levels 0..4.</summary>
        public static readonly byte[][] GamCor = new byte[5][];

        static readonly byte[] flameTab = {
            0xBC,0xBA,0xB8,0xB6,0xB4,0xB2,0xB0,0xD5,0xD6,0xD7,0xA1,0xA0,0xE3,0xE2,0xE1,0xE0
        };

        public static byte[] FlameTable { get { return flameTab; } }

        /// <summary>Loads PLAYPAL/MIXMAP/COLORMAP and builds the derived tables.</summary>
        public static void Load()
        {
            Wad.LoadRes(Wad.GetResId("PLAYPAL"), Main, 0, 0, 768);
            Wad.LoadRes(Wad.GetResId("MIXMAP"), MixMap, 0, 0, MixMap.Length);
            Wad.LoadRes(Wad.GetResId("COLORMAP"), ClrMap, 0, 0, ClrMap.Length);

            for (int i = 0; i < 256; ++i)
                Bright[i] = (byte)(((int)Main[i * 3] + Main[i * 3 + 1] + Main[i * 3 + 2]) * 8 / (63 * 3));

            LoadGammaTable();
            SetGamma(0);
        }

        static void LoadGammaTable()
        {
            for (int i = 0; i < 5; ++i) GamCor[i] = Identity();

            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("Doom2D.gamma.dat"))
            {
                if (s == null) return;
                using (StreamReader r = new StreamReader(s))
                {
                    // The file is a C initialiser: five { .. }, blocks of 64 numbers.
                    string text = r.ReadToEnd();
                    List<int> nums = new List<int>();
                    int i = 0;
                    while (i < text.Length)
                    {
                        if (char.IsDigit(text[i]))
                        {
                            int v = 0;
                            while (i < text.Length && char.IsDigit(text[i]))
                            {
                                v = v * 10 + (text[i] - '0');
                                ++i;
                            }
                            nums.Add(v);
                        }
                        else ++i;
                    }
                    for (int row = 0; row < 5 && (row + 1) * 64 <= nums.Count; ++row)
                        for (int c = 0; c < 64; ++c)
                            GamCor[row][c] = (byte)nums[row * 64 + c];
                }
            }
        }

        static byte[] Identity()
        {
            byte[] t = new byte[64];
            for (int i = 0; i < 64; ++i) t[i] = (byte)i;
            return t;
        }

        /// <summary>setgamma: rebuild std_pal and the RGBA table.</summary>
        public static void SetGamma(int g)
        {
            if (g > 4) g = 4;
            if (g < 0) g = 0;

            byte[] curve = GamCor[g];
            for (int i = 0; i < 256; ++i)
            {
                Std[i * 3] = curve[Main[i * 3]];
                Std[i * 3 + 1] = curve[Main[i * 3 + 1]];
                Std[i * 3 + 2] = curve[Main[i * 3 + 2]];
            }
            RebuildColors();
        }

        /// <summary>VP_set: 6-bit palette values become 8-bit colours (*4, as SDL did).</summary>
        public static void RebuildColors()
        {
            for (int i = 0; i < 256; ++i)
            {
                Colors[i] = new Color(
                    (byte)(Std[i * 3] * 4),
                    (byte)(Std[i * 3 + 1] * 4),
                    (byte)(Std[i * 3 + 2] * 4),
                    255);
            }
        }
    }
}
