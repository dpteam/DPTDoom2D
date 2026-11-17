//
// DPT.DOOM 2D — the VGAED2 image container.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;

namespace DPTDOOM2D.Engine
{
    /// <summary>
    /// Reads the ".VGA" container the Doom2D-era tools used: a palette followed
    /// by a directory of named images.
    ///
    ///     "VGAED2"        7 bytes
    ///     palette[768]    6-bit RGB, the same layout as PLAYPAL
    ///     repeated:       name[40] NUL-terminated, vgaimg header (w,h,sx,sy),
    ///                     then w*h palette indices
    ///     end             an empty name
    ///
    /// The loader is the one from the era's own library (LIBS/VGA/LOADVGA.C), and
    /// the image payload is exactly the vgaimg the engine already knows, so a
    /// prototype's artwork needs no conversion to be drawn by the port.
    /// </summary>
    public static class VgaContainer
    {
        /// <summary>One named image from the container.</summary>
        public sealed class Item
        {
            public string Name;
            public VgaImage Image;
        }

        public sealed class Container
        {
            /// <summary>6-bit RGB, 768 bytes, exactly like PLAYPAL.</summary>
            public byte[] Palette = new byte[768];

            public readonly Dictionary<string, VgaImage> Images =
                new Dictionary<string, VgaImage>(StringComparer.OrdinalIgnoreCase);

            public VgaImage Get(string name)
            {
                VgaImage image;
                return Images.TryGetValue(name, out image) ? image : null;
            }
        }

        public static bool LooksLikeContainer(byte[] data)
        {
            if (data == null || data.Length < 6 + 768) return false;

            const string signature = "VGAED";
            for (int i = 0; i < signature.Length; ++i)
                if (data[i] != (byte)signature[i]) return false;

            return true;
        }

        public static Container Load(string path)
        {
            return Load(File.ReadAllBytes(path));
        }

        /// <summary>
        /// Parses either variant of the container.
        ///
        /// Two exist in the archive, and the sixth byte tells them apart:
        ///
        ///   "VGAED2"  7-byte header, images carry w,h,sx,sy
        ///   "VGAED"   6-byte header, images carry only w,h
        ///
        /// The older one is what the mid-90s games used before the editor grew
        /// hot spots; both store the palette the same way.
        /// </summary>
        public static Container Load(byte[] data)
        {
            if (!LooksLikeContainer(data)) return null;

            bool modern = data[5] == (byte)'2';
            int headerSize = modern ? 7 : 6;

            var container = new Container();
            Buffer.BlockCopy(data, headerSize, container.Palette, 0, 768);

            int at = headerSize + 768;

            while (at < data.Length)
            {
                int nameLength = 0;
                while (at + nameLength < data.Length && nameLength < 40 && data[at + nameLength] != 0)
                    ++nameLength;

                if (nameLength == 0) break;   // empty name ends the directory

                string name = System.Text.Encoding.ASCII.GetString(data, at, nameLength);
                at += nameLength + 1;

                int w, h, sx = 0, sy = 0;

                if (modern)
                {
                    if (at + 8 > data.Length) break;

                    w = BitConverter.ToUInt16(data, at);
                    h = BitConverter.ToUInt16(data, at + 2);
                    sx = BitConverter.ToInt16(data, at + 4);
                    sy = BitConverter.ToInt16(data, at + 6);
                    at += 8;
                }
                else
                {
                    if (at + 4 > data.Length) break;

                    w = BitConverter.ToUInt16(data, at);
                    h = BitConverter.ToUInt16(data, at + 2);
                    at += 4;
                }

                int size = w * h;

                // A malformed entry would walk off the buffer; stop instead.
                if (w <= 0 || h <= 0 || size <= 0 || at + size > data.Length) break;

                var pixels = new byte[size];
                Buffer.BlockCopy(data, at, pixels, 0, size);
                at += size;

                container.Images[name] = new VgaImage(w, h, sx, sy, pixels);
            }

            return container;
        }

        /// <summary>
        /// Installs the container's palette as the engine palette, so the images
        /// look the way they did in 1996.
        ///
        /// The container stores 6-bit values like PLAYPAL, and Palette.SetGamma
        /// expects the same, so this is a straight copy followed by a rebuild.
        /// </summary>
        public static void ApplyPalette(Container container)
        {
            if (container == null) return;

            Buffer.BlockCopy(container.Palette, 0, Palette.Main, 0, 768);
            Palette.RebuildBright();
            Palette.SetGamma(0);
        }
    }
}
