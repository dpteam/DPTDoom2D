//
// DPT.DOOM 2D — the editor's chrome, ported from the DOS GUI library.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using DPTDOOM2D.Engine;

namespace DPTDOOM2D.Editor
{
    /// <summary>
    /// The editor's widgets: frames, buttons, checkboxes, sliders, scroll bars.
    ///
    /// These are ports of the DOS GUI library the original editor was built on
    /// (D2D_Avlibs4g/GUI/GUI.CPP), not an approximation of it. Every colour is
    /// `gray[i]`, every rectangle comes from the same code, and the widgets are
    /// drawn into the 8-bit framebuffer exactly as the DOS ones were — so at 1x,
    /// 3x and 6x the editor is the same picture, just bigger.
    ///
    /// `gray[]` is computed with getbestcolor from the loaded PLAYPAL, the way
    /// GUI_start does it: `gray[i] = getbestcolor(i*4, i*4, i*4)`. Hard-coding
    /// indices would break the moment the WAD changes, and this editor has to work
    /// with three different ones.
    ///
    /// The DOS editor ran with gamma 1 (EDITOR.CFG says `gamma=1`), so the ramp
    /// that reaches the screen is brighter than the raw palette. That is applied
    /// once by the palette, not here: these are palette indices.
    /// </summary>
    internal static class EditorSkin
    {
        /// <summary>gray[16] from GUI_start: the ramp every widget colour comes from.</summary>
        private static readonly byte[] Gray = new byte[16];

        private static VgaContainer.Container icons;

        /// <summary>Loads the ramp and the embedded EDITOR.VGA icons once.</summary>
        public static void Load()
        {
            if (icons != null) return;

            EditorFont.Load();

            for (int i = 0; i < 16; ++i)
                Gray[i] = Palette.GetBestColor(i * 4, i * 4, i * 4);

            try
            {
                byte[] raw = Convert.FromBase64String(EditorAssets.Icons);
                icons = VgaContainer.Load(raw);
            }
            catch (Exception e)
            {
                Log.Warn("editor: cannot read the embedded icons (" + e.Message + ")");
                icons = null;
            }
        }

        /// <summary>One of the original's icons, or null.</summary>
        public static VgaImage Icon(string name)
        {
            Load();
            return icons == null ? null : icons.Get(name);
        }

        /// <summary>A step of the grey ramp: gray[i] in the C code.</summary>
        public static byte G(int i)
        {
            Load();
            return Gray[i < 0 ? 0 : (i > 15 ? 15 : i)];
        }

        // ---- primitives ---------------------------------------------------

        /// <summary>V_clr, in the (x, width, y, height) order the C code used.</summary>
        public static void Clr(int x, int y, int w, int h, byte color)
        {
            Vga.Clr(x, w, y, h, color);
        }

        public static void Dot(int x, int y, byte color)
        {
            Vga.PutPixel(x, y, color);
        }

        // ---- frame --------------------------------------------------------

        /// <summary>frame::draw with the defaults: highlight 10, face 6, shadow 2.</summary>
        public static void Frame(int x, int y, int w, int h)
        {
            Frame(x, y, w, h, 10, 6, 2);
        }

        /// <summary>
        /// frame::draw: a two-pixel bevel around a flat face.
        ///
        /// The asymmetry is deliberate and comes from the C code: the highlight runs
        /// along the top and left, the shadow along the bottom and right, and the
        /// corners are filled with V_dot so they meet without a gap. Note that the
        /// highlight is one pixel shorter at the top and the shadow one pixel wider
        /// at the bottom — copying that exactly is what makes the corners line up.
        /// </summary>
        public static void Frame(int x, int y, int w, int h, int clrh, int clrn, int clrl)
        {
            byte hi = G(clrh), face = G(clrn), lo = G(clrl);

            Clr(x + 2, y + 2, w - 4, h - 4, face);

            Clr(x, y, w - 2, 2, hi);
            Dot(x + w - 2, y, hi);

            Clr(x, y + 2, 2, h - 4, hi);
            Dot(x, y + h - 2, hi);

            Clr(x + 1, y + h - 2, w - 1, 2, lo);
            Dot(x, y + h - 1, lo);

            Clr(x + w - 2, y + 1, 2, h - 3, lo);
            Dot(x + w - 1, y, lo);
        }

        // ---- buttons ------------------------------------------------------

        /// <summary>base_button::draw — the pressed and unpressed bevel.</summary>
        public static void BaseButton(int x, int y, int w, int h, int clrh, int clrn, int clrl, bool pressed)
        {
            int i = pressed ? 1 : 0;

            Clr(x + 1 + i, y + 1 + i, w - 2, h - 2, G(clrn));
            Clr(x + i, y + i, w - 1, 1, G(pressed ? clrl : clrh));
            Clr(x + i, y + i, 1, h - 1, G(pressed ? clrl : clrh));

            if (!pressed)
            {
                Clr(x, y + h - 1, w, 1, G(clrl));
                Clr(x + w - 1, y, 1, h, G(clrl));
            }
            else
            {
                Clr(x, y, w, 1, G(0));
                Clr(x, y, 1, h, G(0));
            }
        }

        /// <summary>
        /// button::draw: the bevel, then the label in black.
        ///
        /// textx is centred by measured width plus one, and texty is (h-5)/2 — the
        /// font's five visible rows, not a cell height.
        /// </summary>
        public static void Button(int x, int y, int w, int h, string text, bool pressed)
        {
            BaseButton(x, y, w, h, 12, 10, 8, pressed);

            if (string.IsNullOrEmpty(text)) return;

            int textX = (w - EditorFont.Width(text)) / 2 + 1;
            int textY = (h - 5) / 2;

            EditorFont.Draw(text, x + textX + (pressed ? 1 : 0), y + textY + (pressed ? 1 : 0), G(0));
        }

        /// <summary>
        /// sprbutton::draw: the icon centred on the button with a black shadow,
        /// and no bevel at all.
        ///
        /// That is why the load and save buttons in the original look like bare
        /// disks: the button is only there to catch the click.
        /// </summary>
        public static void SprButton(int x, int y, int w, int h, string icon, bool pressed)
        {
            VgaImage image = icon == null ? null : Icon(icon);

            if (image == null)
            {
                Button(x, y, w, h, null, pressed);
                return;
            }

            int cx = x + w / 2;
            int cy = y + h / 2;

            Shadow(image, cx + 2, cy + 2);
            Vga.Spr(cx + (pressed ? 1 : 0), cy + (pressed ? 1 : 0), image);
        }

        /// <summary>V_spr1color at +2,+2: the icon's silhouette in gray[0].</summary>
        private static void Shadow(VgaImage image, int x, int y)
        {
            int ox = x - image.SX;
            int oy = y - image.SY;

            byte[] pixels = image.Pixels;

            for (int row = 0; row < image.H; ++row)
            {
                int at = row * image.W;

                for (int col = 0; col < image.W; ++col)
                    if (pixels[at + col] != 0) Vga.PutPixel(ox + col, oy + row, G(0));
            }
        }

        // ---- checkbox -----------------------------------------------------

        /// <summary>
        /// chkbox::draw: a dark border, a mid ring, a light inner face, and a tick.
        ///
        /// The tick is CP866 0xFB, the glyph the DOS font uses as a checkmark — not
        /// a drawn cross. It is placed at width/2-2, height/2-2 from the box corner.
        /// </summary>
        public static void Check(int x, int y, bool on)
        {
            Clr(x, y, 10, 9, G(2));
            Clr(x + 1, y + 1, 9, 8, G(8));
            Clr(x + 1, y + 1, 8, 7, G(4));

            if (on) EditorFont.DrawChar(0xFB, x + 10 / 2 - 2, y + 9 / 2 - 2, G(15));
        }

        // ---- sliders ------------------------------------------------------

        /// <summary>hslider::draw — a well with an 8-pixel thumb.</summary>
        public static void HSlider(int x, int y, int w, int h, int total, int cur)
        {
            Clr(x, y, w, h, G(4));
            Clr(x, y, w - 1, h - 1, G(12));
            Clr(x + 1, y + 1, w - 2, h - 2, G(8));

            int tx = (w - h) * cur / (total > 0 ? total : 1) + x + 1;

            Clr(tx, y + 1, h - 2, h - 2, G(6));
            Clr(tx, y + 1, h - 3, h - 3, G(12));
            Clr(tx + 1, y + 2, h - 4, h - 4, G(10));
        }

        /// <summary>vslider::draw — the same well turned on its side.</summary>
        public static void VSlider(int x, int y, int w, int h, int total, int cur)
        {
            Clr(x, y, w, h, G(4));
            Clr(x, y, w - 1, h - 1, G(12));
            Clr(x + 1, y + 1, w - 2, h - 2, G(8));

            int ty = (h - w) * cur / (total > 0 ? total : 1) + y + 1;

            Clr(x + 1, ty, w - 2, w - 2, G(6));
            Clr(x + 1, ty, w - 3, w - 3, G(12));
            Clr(x + 2, ty + 1, w - 4, w - 4, G(10));
        }

        /// <summary>
        /// vscroll: a slider with an arrow button at each end.
        ///
        /// The buttons are as wide as the bar, so the slider is inset by exactly
        /// that at both ends — which is what makes the arrows look attached.
        /// </summary>
        public static void VScroll(int x, int y, int w, int h, int total, int cur)
        {
            VSlider(x, y + w, w, h - w * 2, total, cur);

            Button(x, y, w, w, "\x18", false);
            Button(x, y + h - w, w, w, "\x19", false);
        }

        public static void HScroll(int x, int y, int w, int h, int total, int cur)
        {
            HSlider(x + h, y, w - h * 2, h, total, cur);

            Button(x, y, h, h, "\x1B", false);
            Button(x + w - h, y, h, h, "\x1A", false);
        }

        // ---- list and sprite view -----------------------------------------

        /// <summary>
        /// strlist::draw: a frame with black inside and one row per entry.
        ///
        /// Rows start four pixels in and step eight, and the loop stops when the
        /// next row would touch the bottom edge — `y+9 &lt; height` in the C code,
        /// which is why a 70-pixel list shows eight rows and not nine.
        /// </summary>
        public static void List(int x, int y, int w, int h, string[] items, int selected, int top)
        {
            Frame(x, y, w, h, 2, 0, 8);

            EditorFont.Clip(x + 4, y + 4, w - 8, h - 8);

            for (int i = top, rowY = 4; i < items.Length && rowY + 9 < h; ++i, rowY += 8)
                EditorFont.Draw(items[i], x + 4, y + rowY, G(i == selected ? 15 : 8));

            EditorFont.ClipAll();
        }

        /// <summary>
        /// sprview::draw: fill the box with one colour, then stamp the image at the
        /// box's corner, clipped to it.
        ///
        /// Not centred: the original stamps at left+sx, top+sy, and the clip is what
        /// keeps a 64x64 texture from spilling onto the panel.
        /// </summary>
        public static void SprView(int x, int y, int w, int h, VgaImage image, int color)
        {
            Clr(x, y, w, h, G(color));

            if (image == null || image.W > 320 || image.H > 200) return;

            Vga.SetRect(x, w, y, h);
            Vga.Spr(x + image.SX, y + image.SY, image);
            Vga.SetRect(0, 320, 0, 200);
        }

        // ---- cursor -------------------------------------------------------

        /// <summary>The mouse cursor: the original's stdarrow, drawn at the pointer.</summary>
        public static void Cursor(int x, int y)
        {
            VgaImage arrow = Icon("stdarrow");

            if (arrow != null)
            {
                Vga.Spr(x, y, arrow);
                return;
            }

            Clr(x, y, 1, 7, G(15));
            Clr(x, y, 7, 1, G(15));
        }
    }
}
