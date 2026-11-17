//
// DPT.DOOM 2D — the editor screen: state, input and the whole UI in the framebuffer.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;
using DPTDOOM2D.Win32;

namespace DPTDOOM2D.Editor
{
    internal enum EditorTool
    {
        Back,
        Wall,
        Front,
        Things,
        Switch
    }

    /// <summary>
    /// The whole editor, laid out exactly as `edit()` in EDIT.CPP does it.
    ///
    /// The original is a 320x200 VGA program: a map view with scroll bars at the
    /// top left, five layer checkboxes beside it, a 104x104 minimap under them, a
    /// tab strip across the bottom and a column of buttons on the right. Every
    /// coordinate below is copied from EDIT.CPP rather than eyeballed, and the
    /// widgets are the DOS GUI library's own — see EditorSkin.
    ///
    ///     view       (0,0,250,110)    map 200x98, hscroll y=100, vscroll x=200
    ///     mapwin     (213,93,104,104) minimap, one pixel per cell
    ///     vcontbox   (0,110,210,90)   five tabs, 20 px strip
    ///     b1         (305,2,13,10)    system menu
    ///     b2         (270,10,16,15)   load
    ///     b3         (270,40,16,15)   save
    ///     b4         (265,70,36,15)   music
    ///     b5         (308,15,6,7)     help
    ///
    /// The camera is the original's (w_x, w_y): the *centre* of the view in map
    /// pixels, not its corner. Everything that converts between the screen and the
    /// map — painting, the minimap, the scroll bars — goes through the same two
    /// formulas the C code uses, so they cannot drift apart.
    /// </summary>
    internal sealed class EditorScreen
    {
        // ---- layout, in framebuffer pixels --------------------------------

        public const int ScreenW = 320;
        public const int ScreenH = 200;

        /// <summary>WD and HT from VIEW.CPP: the map view is 200x98 pixels.</summary>
        public const int ViewX = 0, ViewY = 1, ViewW = 200, ViewH = 98;

        /// <summary>hscroll(0,100,200,10) and vscroll(200,0,10,100).</summary>
        public const int HScrollX = 0, HScrollY = 100, HScrollW = 200, HScrollH = 10;
        public const int VScrollX = 200, VScrollY = 0, VScrollW = 10, VScrollH = 100;

        /// <summary>chkbox(215, i*15+10, 10, 9), one per layer.</summary>
        public const int CheckX = 215, CheckY = 10, CheckStep = 15;

        /// <summary>The layer pictures at V_spr(240, i*15+14).</summary>
        public const int CheckIconX = 240, CheckIconY = 14;

        /// <summary>mapwin(213,93,104,104): a frame, so 100x100 inside.</summary>
        public const int MiniX = 213, MiniY = 93, MiniW = 104, MiniH = 104;

        /// <summary>vcontbox(0,110,210,90) and its 20-pixel tab strip.</summary>
        public const int TabX = 0, TabY = 110, TabW = 210, TabH = 90;
        public const int TabBarW = 20;

        /// <summary>The right column's buttons, at the original's pixels.</summary>
        public const int SysX = 305, SysY = 2, SysW = 13, SysH = 10;
        public const int LoadX = 270, LoadY = 10, LoadW = 16, LoadH = 15;
        public const int SaveX = 270, SaveY = 40, SaveW = 16, SaveH = 15;
        public const int MusicX = 265, MusicY = 70, MusicW = 36, MusicH = 15;
        public const int HelpX = 308, HelpY = 15, HelpW = 6, HelpH = 7;

        /// <summary>The title's centre, from textstr(115,115,1,5,...).</summary>
        public const int TitleX = 115, TitleY = 115;

        // ---- state --------------------------------------------------------

        public MapFile Map;
        public string MapName = "";

        public EditorTool Tool = EditorTool.Back;

        /// <summary>Wall index the brush paints; 0 erases.</summary>
        public int Brush = 1;

        /// <summary>Wall type the Wall tab paints, 0 being "no change".</summary>
        public byte CellBrush = 1;

        /// <summary>Thing the Things tab places.</summary>
        public short ThingBrush = MapThingType.Plr1;

        /// <summary>
        /// The five layer flags. Cell types start off, exactly as EDIT.CPP does it
        /// (`cb[1]->st=0`) — the collision layer hides the artwork, so it is opt-in.
        /// </summary>
        public bool ShowBack = true;
        public bool ShowTypes;
        public bool ShowFront = true;
        public bool ShowThings = true;
        public bool ShowSwitch = true;

        /// <summary>Camera centre in map pixels: w_x and w_y from VIEW.CPP.</summary>
        public int CamX, CamY;

        public bool Changed;
        public bool Help;
        public bool Quit;

        /// <summary>Row picked in the current tab's list.</summary>
        public int ListRow;

        /// <summary>Row picked in the wall-type list of the Wall tab.</summary>
        public int TypeRow;

        /// <summary>Thing being edited, or -1.</summary>
        public int CurThing = -1;

        /// <summary>Trigger being edited, or -1.</summary>
        public int CurSwitch = -1;

        /// <summary>The last thing that happened, for the window title.</summary>
        public string Status = "";

        private int listScroll;

        private Message message;
        private TextPrompt prompt;

        private string path = "";

        // Drag state. The original blocks inside its mouse handler; a frame loop
        // cannot, so the same gesture is split across press, move and release.
        private int dragButton;
        private int dragCellX, dragCellY;
        private int dragLastX, dragLastY;
        private int dragThingX, dragThingY;

        private readonly UndoStack undo = new UndoStack();
        private bool strokeRecorded;

        /// <summary>Cell under the mouse, for the window title.</summary>
        private int hoverX = -1, hoverY = -1;

        // ---- palettes -----------------------------------------------------

        private readonly List<int> wallIndices = new List<int>();
        private readonly List<ThingChoice> things = new List<ThingChoice>();

        /// <summary>wt_name from EDIT.CPP, in its order — index 0 is "no change".</summary>
        public static readonly string[] CellNames =
        {
            "нет", "стена", "стена-дверь", "вода", "поверхн.", "кислота",
            "кислота глуб.", "лава", "ловушка", "лифт вверх", "лифт вниз"
        };

        public static readonly byte[] CellValues =
        {
            0, Game.Cell.Wall, Game.Cell.WallSwapped, Game.Cell.Water, Game.Cell.WaterTop,
            Game.Cell.Acid, Game.Cell.AcidDeep, Game.Cell.Lava, Game.Cell.Trap,
            Game.Cell.LiftUp, Game.Cell.LiftDown
        };

        public EditorScreen()
        {
            BuildThings();
        }

        public void Load(MapFile map, string name, string filePath)
        {
            Map = map;
            MapName = name;
            path = filePath ?? "";
            Changed = false;
            Help = false;
            ListRow = 0;
            TypeRow = 0;
            listScroll = 0;
            CurThing = -1;
            CurSwitch = -1;
            undo.Clear();
            strokeRecorded = false;
            dragButton = 0;

            MapCanvas.Forget();
            BuildWalls();

            // The original opens on the middle of the map: `w_x=FLDW*CELW/2+100`.
            CamX = MapCanvas.MapWidth / 2 + 100;
            CamY = MapCanvas.MapHeight / 2 + 49;
            ClampCamera();

            if (wallIndices.Count > 0) Brush = wallIndices[0];

            Status = map.Summary();
        }

        private void BuildWalls()
        {
            wallIndices.Clear();

            for (int i = 1; i < 256; ++i)
                if (!string.IsNullOrEmpty(Map.WallName[i])) wallIndices.Add(i);
        }

        private void BuildThings()
        {
            things.Clear();

            things.Add(new ThingChoice(MapThingType.Plr1, "1ый игрок"));
            things.Add(new ThingChoice(MapThingType.Plr2, "2ой игрок"));
            things.Add(new ThingChoice(MapThingType.DmStart, "точка DM"));

            string[] items =
            {
                "патроны", "4 гильзы", "1 ракета", "зарядка", "ящик патронов",
                "ящик патронов", "ящик ракет", "ящик зарядов", "аптечка", "медпак",
                "большой медпак", "пила", "дробовик", "дробовик 2", "пулемёт",
                "ракетница", "плазма", "BFG", "душа", "супердробовик",
                "пулемёт 2", "пулемёт 3",
                "мегасфера", "мегасфера", "мегасфера", "мегасфера",
                "невидимость", "невидимость", "невидимость", "невидимость",
                "акваланг", "красный ключ", "зелёный ключ", "синий ключ", "костюм",
                "гиблый"
            };

            for (int i = 0; i < items.Length; ++i)
                things.Add(new ThingChoice((short)(MapThingType.Clip + i), items[i]));

            for (int i = 0; i < Actors.TypeCount; ++i)
                things.Add(new ThingChoice((short)(MapThingType.Demon + i), Actors.NameOf(i + 1)));
        }

        // ------------------------------------------------------------------ loop

        public void Tick(Win32Window window)
        {
            int mx, my;
            window.MouseToFrame(out mx, out my);

            if (message != null)
            {
                message.Tick(window);
                if (message.Done) message = null;
                return;
            }

            if (prompt != null)
            {
                prompt.Tick(window);
                if (prompt.Done)
                {
                    if (prompt.Accepted) ApplyPrompt(prompt.Kind, prompt.Value);
                    prompt = null;
                }

                return;
            }

            HandleKeys(window);
            if (Quit) return;

            HandleMouse(window, mx, my);
            HandleWheel(window, mx, my);
        }

        private void HandleKeys(Win32Window window)
        {
            int[] pressed = window.TakePressed();

            for (int i = 0; i < pressed.Length; ++i)
            {
                int key = pressed[i];

                if (Help) { Help = false; continue; }

                switch (key)
                {
                    // The original has no number keys — the tabs are clicked — but
                    // they are a harmless extra for anyone used to them.
                    case Vk.D1: SelectTool(EditorTool.Back); break;
                    case Vk.D2: SelectTool(EditorTool.Wall); break;
                    case Vk.D3: SelectTool(EditorTool.Front); break;
                    case Vk.D4: SelectTool(EditorTool.Things); break;
                    case Vk.D5: SelectTool(EditorTool.Switch); break;

                    case Vk.Tab:
                        SelectTool((EditorTool)(((int)Tool + 1) % 5));
                        break;

                    // Arrows scroll the view, as `стрелки - прокрутка вида` says.
                    case Vk.Left: CamX -= MapCanvas.CellSize * 2; break;
                    case Vk.Right: CamX += MapCanvas.CellSize * 2; break;
                    case Vk.Up: CamY -= MapCanvas.CellSize * 2; break;
                    case Vk.Down: CamY += MapCanvas.CellSize * 2; break;

                    case Vk.B: ShowBack = !ShowBack; break;
                    case Vk.F: ShowFront = !ShowFront; break;
                    case Vk.T: ShowTypes = !ShowTypes; break;

                    case Vk.F1: ShowHelp(); break;
                    case Vk.F2: AskSave(); break;
                    case Vk.F3: AskOpen(); break;

                    case Vk.Escape:
                        if (Changed) AskSave();
                        else Quit = true;
                        break;

                    case Vk.S:
                        if ((window.Modifiers & Vk.CtrlMask) != 0) AskSave();
                        break;

                    case Vk.O:
                        if ((window.Modifiers & Vk.CtrlMask) != 0) AskOpen();
                        break;

                    case Vk.Z:
                        if ((window.Modifiers & Vk.CtrlMask) != 0) Undo();
                        break;

                    case Vk.N:
                        if ((window.Modifiers & Vk.CtrlMask) != 0) NewMap();
                        break;

                    case Vk.F5: Playtest(); break;
                }
            }

            ClampCamera();
        }

        private void SelectTool(EditorTool tool)
        {
            Tool = tool;
            ListRow = 0;
            listScroll = 0;
        }

        private void HandleMouse(Win32Window window, int mx, int my)
        {
            hoverX = -1;
            hoverY = -1;

            int buttons = window.MouseButtons;

            if (dragButton == 0 && buttons != 0)
            {
                dragButton = buttons;
                strokeRecorded = false;
                Press(mx, my, buttons);
            }
            else if (dragButton != 0 && buttons == 0)
            {
                Release(mx, my, dragButton);
                dragButton = 0;
                strokeRecorded = false;
            }
            else if (dragButton != 0)
            {
                Drag(mx, my, buttons);
            }

            if (InView(mx, my))
            {
                hoverX = (mx + CamX - 100) / MapCanvas.CellSize;
                hoverY = (my + CamY - 50) / MapCanvas.CellSize;
                UpdateStatus();
            }
        }

        private void HandleWheel(Win32Window window, int mx, int my)
        {
            int ticks = window.TakeWheel();
            if (ticks == 0) return;

            if (InTabArea(mx, my))
            {
                listScroll -= ticks * 3;
                ClampList();
                return;
            }

            CamY -= ticks * MapCanvas.CellSize * 2;
            ClampCamera();
        }

        /// <summary>One press: a widget, a tab, the minimap, or the start of a stroke.</summary>
        private void Press(int mx, int my, int buttons)
        {
            if (InTabArea(mx, my)) { PressTab(mx, my); return; }
            if (InMini(mx, my)) { PressMini(mx, my); return; }
            if (InCheck(mx, my)) { PressCheck(mx, my); return; }
            if (PressRight(mx, my)) return;

            if (!InView(mx, my)) return;

            int cx = (mx + CamX - 100) / MapCanvas.CellSize;
            int cy = (my + CamY - 50) / MapCanvas.CellSize;

            dragCellX = dragLastX = cx;
            dragCellY = dragLastY = cy;

            switch (Tool)
            {
                case EditorTool.Things:
                    PressThing(cx, cy, buttons);
                    return;

                case EditorTool.Switch:
                    PressSwitch(cx, cy, buttons);
                    return;
            }

            // Walls: the original paints on release, over the rectangle that was
            // dragged, so a stroke is one undo step and one visible change.
            if (!strokeRecorded)
            {
                undo.Push(Map);
                strokeRecorded = true;
            }
        }

        private void Drag(int mx, int my, int buttons)
        {
            if (Tool == EditorTool.Things && CurThing >= 0 && (buttons & 1) != 0)
            {
                if (!InView(mx, my)) return;

                MapThing thing = Map.Things[CurThing];
                thing.X = (short)(dragThingX + mx - dragLastX);
                thing.Y = (short)(dragThingY + my - dragLastY);

                Changed = true;
                return;
            }

            if (Tool == EditorTool.Things || Tool == EditorTool.Switch) return;
            if (!InView(mx, my)) return;

            dragLastX = (mx + CamX - 100) / MapCanvas.CellSize;
            dragLastY = (my + CamY - 50) / MapCanvas.CellSize;
        }

        private void Release(int mx, int my, int buttons)
        {
            if (Tool == EditorTool.Things || Tool == EditorTool.Switch) return;

            int cx = (mx + CamX - 100) / MapCanvas.CellSize;
            int cy = (my + CamY - 50) / MapCanvas.CellSize;

            if (InView(mx, my)) { dragLastX = cx; dragLastY = cy; }

            FillRect(dragCellX, dragCellY, dragLastX, dragLastY, (buttons & 2) != 0);
        }

        /// <summary>
        /// Fills a cell rectangle with the brush, tiling the texture the way the
        /// original does: a wall wider than a cell is written every ceil(w/8) cells.
        /// </summary>
        private void FillRect(int x1, int y1, int x2, int y2, bool erase)
        {
            int left = Math.Min(x1, x2), right = Math.Max(x1, x2);
            int top = Math.Min(y1, y2), bottom = Math.Max(y1, y2);

            left = Math.Max(0, left); top = Math.Max(0, top);
            right = Math.Min(MapFile.Width - 1, right);
            bottom = Math.Min(MapFile.Height - 1, bottom);

            if (left > right || top > bottom) return;

            int index = erase ? 0 : BrushValue();

            // Step by the texture's size in cells, as `sx=(walp[i]->w+7)/8` does.
            int stepX = 1, stepY = 1;

            if (index > 0)
            {
                VgaImage image = WallImage(index);

                if (image != null)
                {
                    stepX = Math.Max(1, (image.W + 7) / 8);
                    stepY = Math.Max(1, (image.H + 7) / 8);
                }
            }

            byte[] field = FieldOf(Tool);
            if (field == null) return;

            for (int y = top; y <= bottom; ++y)
            {
                if ((y - top) % stepY != 0) continue;

                for (int x = left; x <= right; x += stepX)
                    field[y * MapFile.Width + x] = (byte)index;

                // The wall type is written across the whole row, not tiled: it
                // describes the cell, not the texture.
                if (Tool == EditorTool.Wall)
                {
                    byte type = erase ? (byte)0 : CellBrush;

                    for (int x = left; x <= right; ++x)
                        Map.Type[y * MapFile.Width + x] = type;
                }
            }

            Changed = true;
        }

        /// <summary>The layer a wall tool writes: the original's EWALL picks by type.</summary>
        private byte[] FieldOf(EditorTool tool)
        {
            if (Map == null) return null;

            switch (tool)
            {
                case EditorTool.Back: return Map.Back;
                case EditorTool.Front: return Map.Front;

                // A wall type of 2 or 5..7 means "front", everything else "back" —
                // the same rule as `if(wtype==2 || (wtype>=5 && wtype<=7))`.
                case EditorTool.Wall:
                    return CellBrush == 2 || (CellBrush >= 5 && CellBrush <= 7) ? Map.Front : Map.Back;

                default: return null;
            }
        }

        private int BrushValue()
        {
            return Brush;
        }

        private VgaImage WallImage(int index)
        {
            if (Map == null || index <= 0 || index >= 256) return null;

            // The index is a wall *slot* in this map, not a WAD resource number:
            // Map.WallName[index] is the texture's name. Asking the WAD for resource
            // number `index` returns whatever lump happens to sit there — a sound, a
            // map, anything — and Res.Img on that throws "image truncated".
            string name = Map.WallName[index];
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("_WATER_", StringComparison.Ordinal)) return null;

            int id = Wad.FindRes(name);
            return id < 0 ? null : Res.Img(id);
        }

        // ---- things -------------------------------------------------------

        /// <summary>
        /// ETHING: left picks and drags, right deletes or, on an empty cell, adds.
        ///
        /// That asymmetry is the original's, and it is worth keeping: placing is
        /// what a mapper does most, so it gets the button that does not also drag.
        /// </summary>
        private void PressThing(int cx, int cy, int buttons)
        {
            int mapX = cx * MapCanvas.CellSize + MapCanvas.CellSize / 2;
            int mapY = cy * MapCanvas.CellSize + MapCanvas.CellSize / 2;

            int hit = ThingAt(mapX, mapY);

            if (hit >= 0)
            {
                CurThing = hit;

                if ((buttons & 1) != 0)
                {
                    dragThingX = Map.Things[hit].X;
                    dragThingY = Map.Things[hit].Y;
                    dragLastX = (cx * MapCanvas.CellSize + MapCanvas.CellSize / 2);
                    dragLastY = (cy * MapCanvas.CellSize + MapCanvas.CellSize / 2);
                    undo.Push(Map);
                    strokeRecorded = true;
                }
                else
                {
                    undo.Push(Map);
                    Map.Things.RemoveAt(hit);
                    CurThing = -1;
                    Changed = true;
                }

                return;
            }

            if ((buttons & 2) == 0) { CurThing = -1; return; }
            if (ThingBrush == 0) return;

            undo.Push(Map);

            var placed = new MapThing();
            placed.X = (short)mapX;
            placed.Y = (short)mapY;
            placed.Type = ThingBrush;
            placed.Flags = 0;

            Map.Things.Add(placed);
            CurThing = Map.Things.Count - 1;
            Changed = true;
        }

        /// <summary>TH_isthing: the thing within a few pixels of a map point.</summary>
        private int ThingAt(int mapX, int mapY)
        {
            for (int i = 0; i < Map.Things.Count; ++i)
            {
                int dx = Map.Things[i].X - mapX;
                int dy = Map.Things[i].Y - mapY;

                if (dx > -5 && dx < 5 && dy > -5 && dy < 5) return i;
            }

            return -1;
        }

        // ---- switches -----------------------------------------------------

        /// <summary>
        /// ESWITCH: left picks, both buttons add, right sets the target.
        ///
        /// "Both buttons" is not a typo: the original's help says "нажмите левую
        /// кнопку и, не отпуская её, нажмите правую", and `(b&3)==3` is that.
        /// </summary>
        private void PressSwitch(int cx, int cy, int buttons)
        {
            if (cx < 0 || cy < 0 || cx >= MapFile.Width || cy >= MapFile.Height) return;

            int hit = SwitchAt(cx, cy);

            if (hit >= 0)
            {
                if ((buttons & 3) == 1) CurSwitch = hit;
            }
            else if ((buttons & 3) == 3)
            {
                undo.Push(Map);

                var sw = new MapSwitch();
                sw.X = (byte)cx;
                sw.Y = (byte)cy;
                sw.A = (byte)cx;
                sw.B = (byte)cy;
                sw.Type = 3;              // a door, the commonest trigger
                sw.Flags = 0x03;          // player and monster press it

                Map.Switches.Add(sw);
                CurSwitch = Map.Switches.Count - 1;
                Changed = true;
                return;
            }

            if ((buttons & 3) == 2 && CurSwitch >= 0)
            {
                undo.Push(Map);
                Map.Switches[CurSwitch].A = (byte)cx;
                Map.Switches[CurSwitch].B = (byte)cy;
                Changed = true;
            }
        }

        private int SwitchAt(int cx, int cy)
        {
            for (int i = 0; i < Map.Switches.Count; ++i)
                if (Map.Switches[i].X == cx && Map.Switches[i].Y == cy) return i;

            return -1;
        }

        // ---- tab panel ----------------------------------------------------

        private void PressTab(int mx, int my)
        {
            // The strip: each tab is (TabH-4)/5 = 17 pixels tall, starting at TabY+1.
            if (mx < TabX + TabBarW)
            {
                int row = (my - TabY - 1) * 5 / (TabH - 4);
                if (row >= 0 && row < 5) SelectTool((EditorTool)row);
                return;
            }

            PressTabContent(mx, my);
        }

        private void PressTabContent(int mx, int my)
        {
            switch (Tool)
            {
                case EditorTool.Back:
                case EditorTool.Front:
                    if (PressList(mx, my, 25, 125, 60, 70)) return;
                    if (Hit(mx, my, 145, 125, 60, 10)) { AddWall(); return; }
                    break;

                case EditorTool.Wall:
                    if (Hit(mx, my, 25, 114, 9, 9)) { CellBrush = 0; return; }
                    if (PressList(mx, my, 25, 125, 60, 70)) return;
                    if (PressTypeList(mx, my, 90, 125, 60, 70)) return;
                    if (Hit(mx, my, 155, 125, 50, 10)) { AddWall(); return; }
                    break;

                case EditorTool.Things:
                    if (PressList(mx, my, 25, 125, 85, 70)) return;
                    if (Hit(mx, my, 115, 135, 10, 9)) { FlipThingFlag(0x01); return; }
                    if (Hit(mx, my, 115, 145, 10, 9)) { FlipThingFlag(0x02); return; }
                    break;

                case EditorTool.Switch:
                    if (Hit(mx, my, 25, 123, 9, 9)) { StepSwitch(-1); return; }
                    if (Hit(mx, my, 34, 123, 9, 9)) { StepSwitch(1); return; }
                    if (Hit(mx, my, 25, 140, 40, 9)) { DeleteSwitch(); return; }
                    PressSwitchFlags(mx, my);
                    break;
            }

            if (Hit(mx, my, 199, 113, 6, 7)) ShowHelp();
        }

        /// <summary>strlist2 hit test: rows of eight pixels from four inside.</summary>
        private bool PressList(int mx, int my, int x, int y, int w, int h)
        {
            if (!Hit(mx, my, x, y, w - 10, h)) return false;

            int row = (my - y - 4) / 8 + listScroll;
            if (row < 0 || row >= ListCount()) return false;

            ListRow = row;
            PickListRow();
            return true;
        }

        private bool PressTypeList(int mx, int my, int x, int y, int w, int h)
        {
            if (!Hit(mx, my, x, y, w - 10, h)) return false;

            int row = (my - y - 4) / 8 + TypeRow * 0;
            if (row < 0 || row >= CellNames.Length) return false;

            TypeRow = row;
            CellBrush = CellValues[row];
            return true;
        }

        private void PressSwitchFlags(int mx, int my)
        {
            if (CurSwitch < 0) return;

            int[] xs = { 25, 25, 25, 25, 100, 100, 100 };
            int[] ys = { 155, 165, 175, 185, 155, 165, 175 };
            byte[] bits = { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40 };

            for (int i = 0; i < bits.Length; ++i)
            {
                if (!Hit(mx, my, xs[i], ys[i], 10, 9)) continue;

                undo.Push(Map);

                MapSwitch sw = Map.Switches[CurSwitch];
                sw.Flags = (byte)(sw.Flags ^ bits[i]);
                Changed = true;
                return;
            }
        }

        private void FlipThingFlag(byte bit)
        {
            if (CurThing < 0 || CurThing >= Map.Things.Count) return;

            undo.Push(Map);

            MapThing thing = Map.Things[CurThing];
            thing.Flags = (ushort)(thing.Flags ^ bit);
            Changed = true;
        }

        private void StepSwitch(int delta)
        {
            if (Map.Switches.Count == 0) { CurSwitch = -1; return; }

            if (CurSwitch < 0) CurSwitch = 0;
            else
            {
                CurSwitch += delta;
                if (CurSwitch < 0) CurSwitch = Map.Switches.Count - 1;
                if (CurSwitch >= Map.Switches.Count) CurSwitch = 0;
            }
        }

        private void DeleteSwitch()
        {
            if (CurSwitch < 0 || CurSwitch >= Map.Switches.Count) return;

            undo.Push(Map);
            Map.Switches.RemoveAt(CurSwitch);

            if (CurSwitch >= Map.Switches.Count) CurSwitch = Map.Switches.Count - 1;
            Changed = true;
        }

        private void PressMini(int mx, int my)
        {
            // mapwin::mouse: the click becomes a map pixel, and the scroll bars are
            // set directly — so the view centres on the cell that was clicked.
            int x = mx - MiniX - 2;
            int y = my - MiniY - 2;

            if (x < 0) x = 0; if (x > 99) x = 99;
            if (y < 0) y = 0; if (y > 99) y = 99;

            CamX = x * MapCanvas.CellSize;
            CamY = y * MapCanvas.CellSize;
            ClampCamera();
        }

        private bool PressRight(int mx, int my)
        {
            if (Hit(mx, my, SysX, SysY, SysW, SysH)) { SystemMenu(); return true; }
            if (Hit(mx, my, LoadX, LoadY, LoadW, LoadH)) { AskOpen(); return true; }
            if (Hit(mx, my, SaveX, SaveY, SaveW, SaveH)) { AskSave(); return true; }
            if (Hit(mx, my, HelpX, HelpY, HelpW, HelpH)) { ShowHelp(); return true; }

            if (Hit(mx, my, MusicX, MusicY, MusicW, MusicH))
            {
                message = new Message("Музыка уровня: " +
                    (Map.Music.Length == 0 ? "не задана" : Map.Music) +
                    "\n\nВыбор трека появится вместе с конвертером музыки.");
                return true;
            }

            return false;
        }

        private void PressCheck(int mx, int my)
        {
            for (int row = 0; row < 5; ++row)
            {
                if (!Hit(mx, my, CheckX, CheckY + row * CheckStep, 10, 9)) continue;

                switch (row)
                {
                    case 0: ShowBack = !ShowBack; break;
                    case 1: ShowTypes = !ShowTypes; break;
                    case 2: ShowFront = !ShowFront; break;
                    case 3: ShowThings = !ShowThings; break;
                    case 4: ShowSwitch = !ShowSwitch; break;
                }

                return;
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        /// <summary>view::mouse rejects the border rows, so painting does too.</summary>
        private static bool InView(int x, int y)
        {
            return x >= 0 && x < ViewW && y > 0 && y < ViewH;
        }

        private static bool InTabArea(int x, int y)
        {
            return x >= TabX && x < TabX + TabW && y >= TabY && y < TabY + TabH;
        }

        private static bool InMini(int x, int y)
        {
            return x >= MiniX && x < MiniX + MiniW && y >= MiniY && y < MiniY + MiniH;
        }

        private static bool InCheck(int x, int y)
        {
            return x >= CheckX && x < CheckX + 10 && y >= CheckY && y < CheckY + 4 * CheckStep + 9;
        }

        // ---- lists --------------------------------------------------------

        /// <summary>How many rows the current tab's list has.</summary>
        public int ListCount()
        {
            switch (Tool)
            {
                case EditorTool.Wall: return wallIndices.Count;
                case EditorTool.Things: return things.Count;
                case EditorTool.Switch: return 0;
                default: return wallIndices.Count;
            }
        }

        /// <summary>Name of one row of the current list.</summary>
        public string ListName(int row)
        {
            switch (Tool)
            {
                case EditorTool.Things:
                    return row >= 0 && row < things.Count ? things[row].Name : "";

                default:
                    return row >= 0 && row < wallIndices.Count ? Map.WallName[wallIndices[row]] : "";
            }
        }

        private void PickListRow()
        {
            switch (Tool)
            {
                case EditorTool.Things:
                    if (ListRow >= 0 && ListRow < things.Count) ThingBrush = things[ListRow].Type;
                    break;

                default:
                    if (ListRow >= 0 && ListRow < wallIndices.Count) Brush = wallIndices[ListRow];
                    break;
            }

            UpdateStatus();
        }

        private void ClampList()
        {
            int count = ListCount();
            int visible = 8;

            int max = count - visible;
            if (max < 0) max = 0;

            if (listScroll > max) listScroll = max;
            if (listScroll < 0) listScroll = 0;
        }

        public int ListScroll { get { return listScroll; } }

        // ---- actions ------------------------------------------------------

        private void AddWall()
        {
            // The original opens a dialog with the WAD's texture list. Here the map's
            // own list is what the editor can promise to resolve, so a new name is
            // only accepted if the WAD actually has it.
            message = new Message("Добавить текстуру в список:\n\n" +
                "текстуры берутся из WAD, а не вводятся вручную.\n" +
                "Выберите нужную в списке слева.");
        }

        private void SystemMenu()
        {
            message = new Message("Опции   Справка   Очистка\nНебо    Выход\n\n" +
                "F1 - помощь, F2 - запись, F3 - загрузка,\nF10 - выход.");
        }

        private void ShowHelp()
        {
            message = new Message(
                "Клавиши:\n\n" +
                "F1      - помощь\n" +
                "F2      - запись уровня\n" +
                "F3      - загрузка уровня\n" +
                "F10     - выход из редактора\n" +
                "стрелки - прокрутка вида\n\n" +
                "На виде уровня нажмите левую кнопку мыши\n" +
                "и, не отпуская её, растяните прямоугольник.\n" +
                "Правая кнопка стирает.");
        }

        private void Undo()
        {
            if (!undo.Undo(Map)) { Status = "отменять нечего"; return; }

            BuildWalls();
            ClampList();
            Changed = true;
            Status = "отменено (осталось " + undo.Count + ")";
        }

        private void NewMap()
        {
            if (Changed) { AskSave(); return; }

            Load(MapFile.CreateEmpty(PickWallForNewMap()), "MAP01", "");
            Changed = true;
            Status = "новая карта";
        }

        public static string PickWallForNewMap()
        {
            int res = Wad.FindRes("MAP01");

            if (res >= 0)
            {
                try
                {
                    MapFile sample = MapFile.Read(Wad.LoadRes(res), "MAP01");

                    for (int i = 1; i < 256; ++i)
                    {
                        string name = sample.WallName[i];
                        if (string.IsNullOrEmpty(name)) continue;
                        if (Wad.FindRes(name) < 0) continue;

                        return name;
                    }
                }
                catch { }
            }

            return "SQR01_1";
        }

        private void AskSave()
        {
            string suggested = (MapName.Length == 0 ? "MAP01" : MapName) + ".wad";
            prompt = new TextPrompt("ЗАПИСЬ КАРТЫ:", suggested, TextPromptKind.Save);
        }

        private void AskOpen()
        {
            prompt = new TextPrompt("ЗАГРУЗКА КАРТЫ:", MapName, TextPromptKind.Open);
        }

        private void ApplyPrompt(TextPromptKind kind, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            try
            {
                if (kind == TextPromptKind.Save) Save(value);
                else Open(value);
            }
            catch (Exception e)
            {
                message = new Message("ВНИМАНИЕ!!!\n\nПроизошла ошибка:\n" + Log.Describe(e));
            }
        }

        public bool Save(string fileName)
        {
            string directory = Program.MapsDirectory;
            Directory.CreateDirectory(directory);

            string full = Path.IsPathRooted(fileName) ? fileName : Path.Combine(directory, fileName);
            if (!full.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)) full += ".wad";

            var lumps = new List<KeyValuePair<string, byte[]>>();
            lumps.Add(new KeyValuePair<string, byte[]>(MapName, Map.Write()));

            if (!Prototypes.WadBuilder.WriteLumps(full, lumps))
            {
                Status = "не удалось записать " + full;
                return false;
            }

            path = full;
            Changed = false;
            Status = "сохранено: " + Path.GetFileName(full);
            return true;
        }

        public bool Open(string name)
        {
            string lump = name.Trim().ToUpperInvariant();
            if (lump.Length == 0) return false;

            int res = Wad.FindRes(lump);
            if (res < 0) { Status = "карта " + lump + " не найдена"; return false; }

            Load(MapFile.Read(Wad.LoadRes(res), lump), lump, "");
            return true;
        }

        private void Playtest()
        {
            try
            {
                string directory = Program.MapsDirectory;
                Directory.CreateDirectory(directory);

                string temp = Path.Combine(directory, "_playtest.wad");

                var lumps = new List<KeyValuePair<string, byte[]>>();
                lumps.Add(new KeyValuePair<string, byte[]>(MapName, Map.Write()));

                if (!Prototypes.WadBuilder.WriteLumps(temp, lumps))
                {
                    Status = "не удалось записать тестовую карту";
                    return;
                }

                string game = Program.FindGame();
                if (game == null) { Status = "dptdoom2d.exe не найден рядом"; return; }

                var start = new System.Diagnostics.ProcessStartInfo();
                start.FileName = game;
                start.Arguments = "-file \"" + temp + "\" -warp 1";
                start.UseShellExecute = false;
                start.WorkingDirectory = Path.GetDirectoryName(game);

                System.Diagnostics.Process.Start(start);
                Status = "игра запущена";
            }
            catch (Exception e)
            {
                message = new Message("Ошибка запуска:\n" + Log.Describe(e));
            }
        }

        // ---- view ---------------------------------------------------------

        /// <summary>Centres the view on a map pixel, in the original's coordinates.</summary>
        public void CenterOn(int mapX, int mapY)
        {
            CamX = mapX + 100;
            CamY = mapY + 49;
            ClampCamera();
        }

        /// <summary>W_adjust: the camera cannot show past the level's edge.</summary>
        private void ClampCamera()
        {
            int minX = 100, maxX = MapCanvas.MapWidth - 100;
            int minY = 49, maxY = MapCanvas.MapHeight - 49;

            if (CamX < minX) CamX = minX;
            if (CamX > maxX) CamX = maxX;
            if (CamY < minY) CamY = minY;
            if (CamY > maxY) CamY = maxY;
        }

        private void UpdateStatus()
        {
            if (Map == null) return;

            if (hoverX < 0 || hoverX >= MapFile.Width || hoverY < 0 || hoverY >= MapFile.Height)
            {
                Status = Map.Summary();
                return;
            }

            int at = hoverY * MapFile.Width + hoverX;

            Status = string.Format("{0},{1}  стена {2}  тип {3}",
                hoverX, hoverY, Map.Back[at], Map.Type[at]);
        }

        public int HoverX { get { return hoverX; } }
        public int HoverY { get { return hoverY; } }

        // ---- draw ---------------------------------------------------------
        //
        // Everything below draws into the 320x200 framebuffer. There is no second
        // pass at window resolution: the window stretches this one picture, exactly
        // as it stretches the game, so 1x and 6x show the same editor.

        public void Draw(Win32Window window)
        {
            Vga.SetRect(0, ScreenW, 0, ScreenH);

            if (Map == null) return;

            // desk::draw: the right-hand part of the desktop, gray[5].
            EditorSkin.Clr(200, 0, ScreenW - 200, ScreenH, EditorSkin.G(5));

            DrawView();
            DrawMinimap();
            DrawTabs();
            DrawRightColumn();

            if (message != null) DrawMessage(message);
            else if (prompt != null) DrawPrompt(prompt);

            // A headless frame (--shot) has no window, and therefore no cursor.
            if (window == null) return;

            int mx, my;
            window.MouseToFrame(out mx, out my);
            EditorSkin.Cursor(mx, my);
        }

        /// <summary>view::draw: the map, its bars, the checkboxes and their pictures.</summary>
        private void DrawView()
        {
            MapCanvas.Draw(Map, CamX, CamY,
                ViewX, ViewY, ViewW, ViewH,
                ShowBack, ShowFront, ShowTypes, ShowThings, ShowSwitch,
                CurThing, CurSwitch,
                dragButton != 0 && Tool != EditorTool.Things && Tool != EditorTool.Switch,
                dragCellX, dragCellY, dragLastX, dragLastY);

            // The bars carry the camera as an offset from the view's own half-size,
            // which is why the totals are 600 and 702 rather than the map size.
            EditorSkin.HScroll(HScrollX, HScrollY, HScrollW, HScrollH,
                MapCanvas.MapWidth - ViewW, CamX - 100);

            EditorSkin.VScroll(VScrollX, VScrollY, VScrollW, VScrollH,
                MapCanvas.MapHeight - ViewH, CamY - 49);

            string[] icons = { "back_icon", "wall_icon", "front_icon", "thing_icon", "switch_icon" };
            bool[] values = { ShowBack, ShowTypes, ShowFront, ShowThings, ShowSwitch };

            for (int i = 0; i < 5; ++i)
            {
                EditorSkin.Check(CheckX, CheckY + i * CheckStep, values[i]);

                VgaImage icon = EditorSkin.Icon(icons[i]);
                if (icon != null) Vga.Spr(CheckIconX, CheckIconY + i * CheckStep, icon);
            }
        }

        /// <summary>
        /// mapwin::draw: a frame, then Z_drawmap — the whole level at one pixel per
        /// cell, coloured by *cell type*, not by wall.
        ///
        /// That is not an accident of the implementation: Z_drawmap runs `fld`
        /// through tctab, so the minimap is a collision map. A mapper looks at it to
        /// see where a level is walkable, which the wall layer cannot tell them —
        /// walls are drawn from the back and front layers, and a cell can have both
        /// and still be solid.
        ///
        /// The frame is the original's `clrh=2, clrn=0, clrl=8`: a dark bevel round
        /// a black interior, which is why it reads as a screen.
        /// </summary>
        private void DrawMinimap()
        {
            EditorSkin.Frame(MiniX, MiniY, MiniW, MiniH, 2, 0, 8);

            int x0 = MiniX + 2, y0 = MiniY + 2;

            for (int y = 0; y < MapFile.Height; ++y)
            {
                for (int x = 0; x < MapFile.Width; ++x)
                {
                    int type = Map.Type[y * MapFile.Width + x];
                    if (type == 0) continue;

                    Vga.PutPixel(x0 + x, y0 + y, MapCanvas.TypeColor(type));
                }
            }
        }

        /// <summary>
        /// vcontbox::draw: the five tab pictures on a strip, the active one pressed,
        /// and the active tab's widgets beside it.
        /// </summary>
        private void DrawTabs()
        {
            EditorSkin.Frame(TabX, TabY, TabW, TabH);

            int strip = TabH - 4;              // 86 usable pixels, as in the C code
            int cur = (int)Tool;

            // The separator between the strip and the content, drawn only up to the
            // current tab's row — that is what makes the strip read as one control.
            EditorSkin.Clr(TabX + TabBarW, TabY + 2, 1, strip * cur / 5, EditorSkin.G(10));
            EditorSkin.Clr(TabX + TabBarW, TabY + 2 + strip * cur / 5, 1,
                strip * (cur + 1) / 5 - strip * cur / 5, EditorSkin.G(10));
            EditorSkin.Clr(TabX + TabBarW, TabY + 1 + strip * (cur + 1) / 5, 1,
                strip - strip * (cur + 1) / 5, EditorSkin.G(10));

            string[] icons = { "back_icon", "wall_icon", "front_icon", "thing_icon", "switch_icon" };

            for (int i = 0; i < 5; ++i)
            {
                int bottom = TabY + 1 + strip * (i + 1) / 5;

                EditorSkin.Clr(TabX + 2, bottom, TabBarW - 2, 1,
                    EditorSkin.G(i + 1 == cur ? 10 : 2));

                VgaImage icon = EditorSkin.Icon(icons[i]);

                if (icon != null)
                    Vga.Spr(TabX + TabBarW / 2 + (i == cur ? 1 : 0), bottom - TabH / 5 / 2, icon);
            }

            string title = TitleOf(Tool);
            EditorFont.DrawCentered(title, TitleX, TitleY, EditorSkin.G(15));

            switch (Tool)
            {
                case EditorTool.Back: DrawBackTab(); break;
                case EditorTool.Wall: DrawWallTab(); break;
                case EditorTool.Front: DrawFrontTab(); break;
                case EditorTool.Things: DrawThingsTab(); break;
                case EditorTool.Switch: DrawSwitchTab(); break;
            }
        }

        private static string TitleOf(EditorTool tool)
        {
            switch (Tool0(tool))
            {
                case EditorTool.Back: return "ФОН";
                case EditorTool.Wall: return "СТЕНЫ, ДВЕРИ И Т.П.";
                case EditorTool.Front: return "ПЕРЕДНИЙ ПЛАН";
                case EditorTool.Things: return "ВЕЩИ, МОНСТРЫ И Т.П.";
                default: return "КНОПКИ";
            }
        }

        private static EditorTool Tool0(EditorTool tool) { return tool; }

        /// <summary>The Back and Front tabs: a texture list, a preview, and "Добавить".</summary>
        private void DrawBackTab()
        {
            // strlist2(25,125,60,70): 50 pixels of list, 10 of scroll bar.
            DrawWallList(25, 125, 60, 70);

            EditorSkin.Button(145, 125, 60, 10, "Добавить", false);

            EditorSkin.SprView(145, 140, 60, 55, PreviewImage(), 0);

            DrawTabHelp(199, 113);
        }

        private void DrawFrontTab()
        {
            DrawWallList(25, 125, 60, 70);

            EditorSkin.Button(145, 125, 60, 10, "Добавить", false);

            EditorSkin.SprView(145, 140, 60, 55, PreviewImage(), 0);

            DrawTabHelp(199, 113);
        }

        /// <summary>The Wall tab: textures, the type list, the type's own preview.</summary>
        private void DrawWallTab()
        {
            EditorSkin.Button(25, 114, 9, 9, "", false);

            DrawWallList(25, 125, 60, 70);

            // wtl1(90,125,60,70): the type list, with its own bar.
            EditorSkin.List(90, 125, 50, 70, CellNames, TypeRow, 0);
            EditorSkin.VScroll(140, 125, 10, 70, Math.Max(0, CellNames.Length - 1), 0);

            EditorSkin.Button(155, 125, 50, 10, "Добавить", false);

            EditorSkin.SprView(155, 140, 50, 55, PreviewImage(), 0);

            DrawTabHelp(199, 113);
        }

        private void DrawThingsTab()
        {
            string[] names = new string[things.Count];
            for (int i = 0; i < things.Count; ++i) names[i] = things[i].Name;

            // strlist2(25,125,85,70): the list is 75 wide and the bar takes the rest.
            EditorSkin.List(25, 125, 75, 70, names, ListRow, listScroll);
            EditorSkin.VScroll(100, 125, 10, 70, Math.Max(0, things.Count - 1), listScroll);

            EditorFont.DrawCentered(CurrentThingName, TitleX, 127, EditorSkin.G(12));

            bool right = CurThing >= 0 && (Map.Things[CurThing].Flags & 0x01) != 0;
            bool dm = CurThing >= 0 && (Map.Things[CurThing].Flags & 0x02) != 0;

            EditorSkin.Check(115, 135, right);
            EditorFont.Draw("вправо", 130, 137, EditorSkin.G(15));

            EditorSkin.Check(115, 145, dm);
            EditorFont.Draw("только в DM", 130, 147, EditorSkin.G(15));

            DrawTabHelp(199, 113);
        }

        /// <summary>The Switch tab: prev/next, delete, the name, and the seven flags.</summary>
        private void DrawSwitchTab()
        {
            EditorSkin.Button(25, 123, 9, 9, "\x1B", false);
            EditorSkin.Button(34, 123, 9, 9, "\x1A", false);
            EditorSkin.Button(25, 140, 40, 9, "Убрать", false);

            EditorFont.Draw(CurrentSwitchName, 45, 125, EditorSkin.G(12));

            string[] labels =
            {
                "игрок наж.", "монстр наж.", "игрок близко", "монстр близко",
                "красный ключ", "зеленый ключ", "синий ключ"
            };

            int[] xs = { 25, 25, 25, 25, 100, 100, 100 };
            int[] ys = { 155, 165, 175, 185, 155, 165, 175 };
            byte[] bits = { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40 };

            byte flags = CurSwitch >= 0 && CurSwitch < Map.Switches.Count
                ? Map.Switches[CurSwitch].Flags : (byte)0;

            for (int i = 0; i < labels.Length; ++i)
            {
                EditorSkin.Check(xs[i], ys[i], (flags & bits[i]) != 0);
                EditorFont.Draw(labels[i], xs[i] + 15, ys[i] + 2, EditorSkin.G(15));
            }

            DrawTabHelp(199, 113);
            DrawTabHelp(120, 123);
        }

        private void DrawTabHelp(int x, int y)
        {
            VgaImage icon = EditorSkin.Icon("help_icon");

            if (icon != null) Vga.Spr(x + 3, y + 3, icon);
        }

        /// <summary>strlist2: the list plus its own scroll bar, ten pixels wide.</summary>
        private void DrawWallList(int x, int y, int w, int h)
        {
            string[] names = new string[wallIndices.Count];
            for (int i = 0; i < wallIndices.Count; ++i) names[i] = Map.WallName[wallIndices[i]];

            EditorSkin.List(x, y, w - 10, h, names, ListRow, listScroll);
            EditorSkin.VScroll(x + w - 10, y, 10, h, Math.Max(0, wallIndices.Count - 1), listScroll);
        }

        /// <summary>
        /// The texture the preview box shows: the wall picked in the list.
        ///
        /// The original keeps one preview per wall tab (`wlsv[0..2]`) and fills it
        /// when the list selection changes, which is why an untouched tab shows
        /// black — the box is there, the picture is not.
        /// </summary>
        private VgaImage PreviewImage()
        {
            if (Map == null || Brush <= 0 || Brush >= 256) return null;

            string name = Map.WallName[Brush];
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("_WATER_", StringComparison.Ordinal)) return null;

            int id = Wad.FindRes(name);
            return id < 0 ? null : Res.Img(id);
        }
        /// <summary>
        /// The right column: system, load, save, help, music.
        ///
        /// sprbutton draws no bevel — the disk icons are the buttons — so these are
        /// pictures with a hit rectangle, not frames.
        /// </summary>
        private void DrawRightColumn()
        {
            EditorSkin.SprButton(SysX, SysY, SysW, SysH, "system_icon", false);
            EditorSkin.SprButton(LoadX, LoadY, LoadW, LoadH, "load_icon", false);
            EditorSkin.SprButton(SaveX, SaveY, SaveW, SaveH, "save_icon", false);
            EditorSkin.SprButton(HelpX, HelpY, HelpW, HelpH, "help_icon", false);

            EditorSkin.Button(MusicX, MusicY, MusicW, MusicH, "Музыка", false);
        }

        /// <summary>
        /// mdialog: a frame with the text in the small font and an OK button.
        ///
        /// Sized to the text, centred on the screen, and drawn over everything —
        /// the original's message() boxes look exactly like this.
        /// </summary>
        private static void DrawMessage(Message message)
        {
            string[] lines = message.Lines;

            int textW = 0;

            for (int i = 0; i < lines.Length; ++i)
            {
                int lineW = EditorFont.Width(lines[i]);
                if (lineW > textW) textW = lineW;
            }

            int w = Math.Min(ScreenW - 20, Math.Max(120, textW + 24));
            int h = lines.Length * 8 + 34;

            int x = (ScreenW - w) / 2;
            int y = (ScreenH - h) / 2;

            EditorSkin.Frame(x, y, w, h);

            int lineY = y + 8;

            for (int i = 0; i < lines.Length; ++i)
            {
                EditorFont.DrawCentered(lines[i], x + w / 2, lineY, EditorSkin.G(15));
                lineY += 8;
            }

            EditorSkin.Button(x + w / 2 - 25, y + h - 16, 50, 11, "OK", false);
        }

        private void DrawPrompt(TextPrompt prompt)
        {
            int w = 200, h = 30;
            int x = (ScreenW - w) / 2;
            int y = (ScreenH - h) / 2;

            EditorSkin.Frame(x, y, w, h);

            EditorFont.DrawCentered(prompt.Label, x + w / 2, y + 6, EditorSkin.G(15));

            EditorSkin.Clr(x + 10, y + 16, w - 20, 10, EditorSkin.G(0));
            EditorFont.Draw(prompt.Value + "_", x + 12, y + 18, EditorSkin.G(15));
        }

        // ---- names for the UI ----------------------------------------------

        public string CurrentWallName
        {
            get
            {
                if (Map == null || Brush <= 0 || Brush >= 256) return "";
                return Map.WallName[Brush];
            }
        }

        public string CurrentThingName
        {
            get
            {
                // th_cn is (curth==-1) ? "" : TH_getname(...): the name belongs to
                // the thing being edited, not to the brush. Showing the brush's name
                // put a second "1ый игрок" over the panel with nothing selected.
                if (CurThing < 0 || CurThing >= Map.Things.Count) return "";

                for (int i = 0; i < things.Count; ++i)
                    if (things[i].Type == Map.Things[CurThing].Type) return things[i].Name;

                return "";
            }
        }

        public string CurrentSwitchName
        {
            get
            {
                if (CurSwitch < 0 || CurSwitch >= Map.Switches.Count) return "";
                return SwitchNames.NameOf(Map.Switches[CurSwitch].Type);
            }
        }

        public string CellName
        {
            get
            {
                for (int i = 0; i < CellValues.Length; ++i)
                    if (CellValues[i] == CellBrush) return CellNames[i];
                return "";
            }
        }

        public string FileName { get { return path; } }
    }

    /// <summary>The trigger names, from SW_getname in SWITCH.C.</summary>
    internal static class SwitchNames
    {
        public static string NameOf(int type)
        {
            switch (type)
            {
                case 1: return "открыть дверь";
                case 2: return "закрыть дверь";
                case 3: return "дверь";
                case 4: return "дверь 5";
                case 5: return "нажать";
                case 6: return "телепорт";
                case 7: return "лифт";
                case 8: return "лифт вверх";
                case 9: return "лифт вниз";
                case 10: return "ловушка";
                case 11: return "закрыть ловушку";
                default: return "кнопка " + type;
            }
        }
    }

    /// <summary>A modal message box, as mdialog draws one.</summary>
    internal sealed class Message
    {
        public readonly string[] Lines;
        public bool Done;

        /// <summary>
        /// True until the button that opened the box has been released.
        ///
        /// Without this the box closes on the very click that opened it: the frame
        /// that sets `message` still has the button down, and the release a moment
        /// later looks like a click on the box. The original could not have the
        /// problem — it blocked inside message() until the button came back up.
        /// </summary>
        private bool armed;

        public Message(string text)
        {
            Lines = (text ?? "").Replace("\r", "").Split('\n');
        }

        public void Tick(Win32Window window)
        {
            if (window.TakePressed().Length > 0) { Done = true; return; }

            // Wait for the opening click to finish before any click counts.
            if (window.MouseButtons == 0) { armed = true; return; }

            if (armed) Done = true;
        }
    }

    internal sealed class ThingChoice
    {
        public readonly short Type;
        public readonly string Name;

        public ThingChoice(short type, string name)
        {
            Type = type;
            Name = name;
        }
    }

    /// <summary>
    /// A one-line text prompt drawn on the framebuffer, like the original's inputbox.
    /// </summary>
    internal sealed class TextPrompt
    {
        public readonly string Label;
        public readonly TextPromptKind Kind;
        public string Value = "";

        public bool Done;
        public bool Accepted;

        public TextPrompt(string label, string value, TextPromptKind kind)
        {
            Label = label;
            Kind = kind;
            Value = value ?? "";
        }

        public void Tick(Win32Window window)
        {
            int[] pressed = window.TakePressed();

            for (int i = 0; i < pressed.Length; ++i)
            {
                int key = pressed[i];

                if (key == Vk.Return) { Accepted = true; Done = true; return; }
                if (key == Vk.Escape) { Accepted = false; Done = true; return; }

                if (key == Vk.Back) { if (Value.Length > 0) Value = Value.Substring(0, Value.Length - 1); continue; }

                char c = Vk.ToChar(key);
                if (c != '\0' && Value.Length < 40) Value += c;
            }
        }
    }

    internal enum TextPromptKind
    {
        Save,
        Open
    }

    /// <summary>Virtual-key codes and the few conversions the editor needs.</summary>
    internal static class Vk
    {
        public const int Back = 8;
        public const int Tab = 9;
        public const int Return = 13;
        public const int Escape = 27;
        public const int Left = 37;
        public const int Up = 38;
        public const int Right = 39;
        public const int Down = 40;

        public const int D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34, D5 = 0x35;
        public const int B = 0x42, F = 0x46, T = 0x54;
        public const int N = 0x4E, O = 0x4F, S = 0x53, Z = 0x5A;

        public const int F1 = 0x70, F2 = 0x71, F3 = 0x72, F5 = 0x74, F10 = 0x79;

        public const int ShiftMask = 1, CtrlMask = 2, AltMask = 4;

        public static char ToChar(int key)
        {
            if (key >= 0x30 && key <= 0x39) return (char)key;
            if (key >= 0x41 && key <= 0x5A) return (char)('a' + (key - 0x41));

            switch (key)
            {
                case 0xBE: return '.';
                case 0xBF: return '/';
                case 0xBD: return '-';
                case 0xDB: return '[';
                case 0xDD: return ']';
                default: return '\0';
            }
        }
    }
}
