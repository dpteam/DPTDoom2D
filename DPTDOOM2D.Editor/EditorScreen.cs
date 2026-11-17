//
// DPT.DOOM 2D — the editor screen: state, input and the framebuffer part of the UI.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DPTDOOM2D.Engine;
using DPTDOOM2D.Game;
using DPTDOOM2D.Win32;

namespace DPTDOOM2D.Editor
{
    internal enum EditorTool
    {
        Back,
        Front,
        Type,
        Things
    }

    /// <summary>
    /// The whole editor: one 320x200 screen, keyboard and mouse, no controls.
    ///
    /// This is how the original worked — the editor was a VGA program with its own
    /// panels drawn on the game's framebuffer, not a Windows application. The map is
    /// drawn with the game's textures in the framebuffer; the labels and boxes are
    /// drawn on top with GDI (see <see cref="EditorUi"/>), because that is the only
    /// way to get Cyrillic without shipping a bitmap font.
    /// </summary>
    internal sealed class EditorScreen
    {
        // ---- layout, in framebuffer pixels --------------------------------

        public const int ScreenW = 320;
        public const int ScreenH = 200;

        /// <summary>The map viewport: the original put it at the top left.</summary>
        public const int ViewX = 0, ViewY = 0, ViewW = 200, ViewH = 140;

        /// <summary>The tool and status panel on the right.</summary>
        public const int PanelX = 200, PanelY = 0, PanelW = 120, PanelH = 200;

        /// <summary>The palette strip under the map.</summary>
        public const int PaletteX = 0, PaletteY = 140, PaletteW = 200, PaletteH = 60;

        /// <summary>Palette tile: 16x16 picture plus a two-pixel gap.</summary>
        public const int Tile = 18;
        public const int TilesPerRow = PaletteW / Tile;

        // ---- state --------------------------------------------------------

        public MapFile Map;
        public string MapName = "";

        public EditorTool Tool = EditorTool.Back;

        /// <summary>Wall index the brush paints, 0 erases.</summary>
        public int Brush = 1;

        /// <summary>Cell type the Type tool paints.</summary>
        public byte CellBrush = 1;

        /// <summary>Thing the Things tool places.</summary>
        public short ThingBrush = MapThingType.Plr1;

        public bool ShowBack = true;
        public bool ShowFront = true;
        public bool ShowTypes = true;

        /// <summary>Map pixel at the top-left of the viewport.</summary>
        public int CamX, CamY;

        /// <summary>First visible palette entry: the strip scrolls.</summary>
        public int PaletteScroll;

        public bool Changed;
        public string Status = "";
        public string Hint = "";

        /// <summary>True while the help overlay is up.</summary>
        public bool Help;

        /// <summary>Set when the editor wants to quit.</summary>
        public bool Quit;

        /// <summary>Pending "type a name" prompt, null when idle.</summary>
        private TextPrompt prompt;

        private string path = "";
        private int dragButton;
        private int dragMouseX, dragMouseY;
        private int dragCamX, dragCamY;
        private bool panning;

        /// <summary>Cell under the mouse, for the status line.</summary>
        private int hoverX = -1, hoverY = -1;

        // ---- palettes -----------------------------------------------------

        /// <summary>Walls that this map actually uses, in index order.</summary>
        private readonly List<int> wallIndices = new List<int>();

        private readonly List<ThingChoice> things = new List<ThingChoice>();

        /// <summary>The cell types, with the names a mapper needs.</summary>
        /// <summary>Cell types, in palette order: the Type tool draws these as a list.</summary>
        public static readonly string[] CellNames =
        {
            "пусто", "стена", "стена-дверь", "вода", "поверхность", "кислота",
            "кислота глуб.", "лава", "ловушка", "лифт вверх", "лифт вниз", "ловушка закр."
        };

        public static readonly byte[] CellValues =
        {
            Game.Cell.Empty, Game.Cell.Wall, Game.Cell.WallSwapped, Game.Cell.Water,
            Game.Cell.WaterTop, Game.Cell.Acid, Game.Cell.AcidDeep, Game.Cell.Lava,
            Game.Cell.Trap, Game.Cell.LiftUp, Game.Cell.LiftDown, Game.Cell.TrapClosed
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
            PaletteScroll = 0;

            MapCanvas.Forget();
            BuildWalls();

            // Open on the player start, so the first screen shows the level rather
            // than an empty corner.
            int spawn = FindSpawn();
            if (spawn >= 0)
                CenterOn(Map.Things[spawn].X, Map.Things[spawn].Y);

            if (wallIndices.Count > 1) Brush = wallIndices[1];

            Status = map.Summary();
        }

        private int FindSpawn()
        {
            for (int i = 0; i < Map.Things.Count; ++i)
                if (Map.Things[i].Type == MapThingType.Plr1) return i;

            return Map.Things.Count > 0 ? 0 : -1;
        }

        private void BuildWalls()
        {
            wallIndices.Clear();

            for (int i = 1; i < 256; ++i)
            {
                string name = Map.WallName[i];
                if (!string.IsNullOrEmpty(name)) wallIndices.Add(i);
            }
        }

        private void BuildThings()
        {
            things.Clear();

            things.Add(new ThingChoice(MapThingType.Plr1, "Игрок 1"));
            things.Add(new ThingChoice(MapThingType.Plr2, "Игрок 2"));
            things.Add(new ThingChoice(MapThingType.DmStart, "Deathmatch"));

            string[] items =
            {
                "Обойма", "Патроны 4", "Ракеты", "Заряды", "Патроны", "Ящик патр.",
                "Ящик ракет", "Ящик зар.", "Аптечка", "Медпак", "Большой медпак",
                "Пила", "Дробовик", "Дробовик 2", "Пулемёт", "Ракетница", "Плазма", "BFG",
                "Душа", "Супердроб.", "Пулемёт 2", "Пулемёт 3",
                "Мегасфера", "Мегасфера", "Мегасфера", "Мегасфера",
                "Невидимость", "Невидимость", "Невидимость", "Невидимость",
                "Акваланг", "Красный ключ", "Зелёный ключ", "Синий ключ", "Костюм",
                "Гиблый"
            };

            for (int i = 0; i < items.Length; ++i)
                things.Add(new ThingChoice((short)(MapThingType.Clip + i), items[i]));

            for (int i = 0; i < Actors.TypeCount; ++i)
                things.Add(new ThingChoice((short)(MapThingType.Demon + i), Actors.NameOf(i + 1)));
        }

        // ------------------------------------------------------------------ loop

        /// <summary>One frame: input, then draw.</summary>
        public void Tick(Win32Window window)
        {
            int mx, my;
            window.MouseToFrame(out mx, out my);

            if (prompt != null)
            {
                prompt.Tick(window);
                if (prompt.Done)
                {
                    if (prompt.Accepted) ApplyPrompt(prompt.Kind, prompt.Value);
                    prompt = null;
                }

                Draw(window);
                return;
            }

            HandleKeys(window);
            if (Quit) return;

            HandleMouse(window, mx, my);
            HandleWheel(window, mx, my);

            Draw(window);
        }

        private void HandleKeys(Win32Window window)
        {
            int[] pressed = window.TakePressed();

            for (int i = 0; i < pressed.Length; ++i)
            {
                int key = pressed[i];

                if (Help)
                {
                    // Any key closes the help, like the original's message box.
                    Help = false;
                    continue;
                }

                switch (key)
                {
                    case Vk.D1: Tool = EditorTool.Back; break;
                    case Vk.D2: Tool = EditorTool.Front; break;
                    case Vk.D3: Tool = EditorTool.Type; break;
                    case Vk.D4: Tool = EditorTool.Things; break;

                    case Vk.B: ShowBack = !ShowBack; break;
                    case Vk.F: ShowFront = !ShowFront; break;
                    case Vk.T: ShowTypes = !ShowTypes; break;

                    case Vk.F1: Help = true; break;

                    case Vk.Left: CamX -= MapCanvas.CellSize * 2; break;
                    case Vk.Right: CamX += MapCanvas.CellSize * 2; break;
                    case Vk.Up: CamY -= MapCanvas.CellSize * 2; break;
                    case Vk.Down: CamY += MapCanvas.CellSize * 2; break;

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

                    case Vk.F5: Playtest(); break;
                }
            }

            ClampCamera();
        }

        private void HandleMouse(Win32Window window, int mx, int my)
        {
            hoverX = -1;
            hoverY = -1;

            int buttons = window.MouseButtons;

            // A press starts a gesture; the gesture ends when the button is released.
            if (dragButton == 0 && buttons != 0)
            {
                dragButton = buttons;
                dragMouseX = mx;
                dragMouseY = my;
                dragCamX = CamX;
                dragCamY = CamY;

                // Middle button, or right with the left one held, pans the map.
                panning = (buttons & 4) != 0;
                if (!panning) Press(mx, my, buttons);
            }
            else if (dragButton != 0 && buttons == 0)
            {
                dragButton = 0;
                panning = false;
            }
            else if (dragButton != 0)
            {
                if (panning || (buttons & 4) != 0)
                {
                    CamX = dragCamX - (mx - dragMouseX);
                    CamY = dragCamY - (my - dragMouseY);
                    ClampCamera();
                }
                else if ((buttons & 1) != 0 && InView(mx, my) && Tool != EditorTool.Things)
                {
                    // Dragging with the left button keeps painting, like a brush.
                    BrushAt(mx, my, (dragButton & 2) != 0 ? 0 : BrushValue());
                }
            }

            if (InView(mx, my))
            {
                hoverX = (mx - ViewX + CamX) / MapCanvas.CellSize;
                hoverY = (my - ViewY + CamY) / MapCanvas.CellSize;
                UpdateStatus();
            }
        }

        private void HandleWheel(Win32Window window, int mx, int my)
        {
            int ticks = window.TakeWheel();
            if (ticks == 0) return;

            if (InPalette(mx, my))
            {
                PaletteScroll -= ticks * TilesPerRow;
                ClampPalette();
                return;
            }

            CamY -= ticks * MapCanvas.CellSize * 2;
            ClampCamera();
        }

        /// <summary>One press: a palette pick, a tool pick, or a paint stroke.</summary>
        private void Press(int mx, int my, int buttons)
        {
            if (InPalette(mx, my))
            {
                PickPalette(mx, my);
                return;
            }

            if (mx >= PanelX)
            {
                PickPanel(mx, my);
                return;
            }

            if (!InView(mx, my)) return;

            bool erase = (buttons & 2) != 0;

            if (Tool == EditorTool.Things)
            {
                PlaceThing(mx, my, erase);
                return;
            }

            BrushAt(mx, my, erase ? 0 : BrushValue());
        }

        private int BrushValue()
        {
            switch (Tool)
            {
                case EditorTool.Type: return CellBrush;
                default: return Brush;
            }
        }

        private void PickPanel(int mx, int my)
        {
            // Four tool rows, then three layer toggles: the same order they are
            // drawn in EditorUi, so the hit boxes cannot drift from the labels.
            int row = (my - EditorUi.ToolTop) / EditorUi.RowHeight;

            if (row >= 0 && row < 4)
            {
                Tool = (EditorTool)row;
                return;
            }

            int toggle = (my - EditorUi.ToggleTop) / EditorUi.RowHeight;

            if (toggle == 0) ShowBack = !ShowBack;
            else if (toggle == 1) ShowFront = !ShowFront;
            else if (toggle == 2) ShowTypes = !ShowTypes;
        }

        private void PickPalette(int mx, int my)
        {
            int column = (mx - PaletteX) / Tile;
            int row = (my - PaletteY) / Tile;
            int index = PaletteScroll + row * TilesPerRow + column;

            if (Tool == EditorTool.Type)
            {
                // The type palette is a two-column list, not a tile grid.
                int at = row + ((mx - PaletteX) >= PaletteW / 2 ? 1 : 0) * 6;
                if (at >= 0 && at < CellValues.Length) CellBrush = CellValues[at];
                return;
            }

            if (Tool == EditorTool.Things)
            {
                if (index >= 0 && index < things.Count)
                {
                    ThingBrush = things[index].Type;
                    Status = things[index].Name;
                }

                return;
            }

            if (index >= 0 && index < wallIndices.Count) Brush = wallIndices[index];
        }

        private void BrushAt(int mx, int my, int value)
        {
            if (Map == null) return;

            int cx = (mx - ViewX + CamX) / MapCanvas.CellSize;
            int cy = (my - ViewY + CamY) / MapCanvas.CellSize;

            if (cx < 0 || cy < 0 || cx >= MapFile.Width || cy >= MapFile.Height) return;

            int at = cy * MapFile.Width + cx;

            switch (Tool)
            {
                case EditorTool.Back:
                    if (Map.Back[at] == value) return;
                    Map.Back[at] = (byte)value;
                    break;

                case EditorTool.Front:
                    if (Map.Front[at] == value) return;
                    Map.Front[at] = (byte)value;
                    break;

                case EditorTool.Type:
                    if (Map.Type[at] == value) return;
                    Map.Type[at] = (byte)value;
                    break;

                default:
                    return;
            }

            Changed = true;
        }

        private void PlaceThing(int mx, int my, bool remove)
        {
            int mapX = mx - ViewX + CamX;
            int mapY = my - ViewY + CamY;

            // Remove the nearest thing within half a cell, otherwise add one.
            MapThing nearest = null;
            int best = MapCanvas.CellSize * MapCanvas.CellSize / 4;

            for (int i = 0; i < Map.Things.Count; ++i)
            {
                MapThing thing = Map.Things[i];
                int dx = thing.X - mapX, dy = thing.Y - mapY;
                int distance = dx * dx + dy * dy;

                if (distance < best) { best = distance; nearest = thing; }
            }

            if (remove)
            {
                if (nearest == null) return;

                Map.Things.Remove(nearest);
                Changed = true;
                return;
            }

            var placed = new MapThing();
            placed.X = (short)mapX;
            placed.Y = (short)mapY;
            placed.Type = ThingBrush;
            placed.Flags = 0;
            Map.Things.Add(placed);

            Changed = true;
        }

        // ------------------------------------------------------------------ save

        private void AskSave()
        {
            string suggested = (MapName.Length == 0 ? "MAP01" : MapName) + ".wad";
            prompt = new TextPrompt("Сохранить как (WAD):", suggested, TextPromptKind.Save);
        }

        private void AskOpen()
        {
            prompt = new TextPrompt("Открыть карту:", MapName, TextPromptKind.Open);
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
                Status = "ошибка: " + Log.Describe(e);
            }
        }

        /// <summary>
        /// Writes the map as a one-lump PWAD named after the map, into Data/Maps.
        ///
        /// The game loads it with -file; --mapcheck proves the bytes are the ones the
        /// reader expects, so a map saved here is a map the game can play.
        /// </summary>
        public bool Save(string fileName)
        {
            string directory = Program.MapsDirectory;
            Directory.CreateDirectory(directory);

            string full = Path.IsPathRooted(fileName) ? fileName : Path.Combine(directory, fileName);
            if (!full.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)) full += ".wad";

            byte[] data = Map.Write();

            var lumps = new List<KeyValuePair<string, byte[]>>();
            lumps.Add(new KeyValuePair<string, byte[]>(MapName, data));

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

        /// <summary>Loads a map lump by name from the loaded WADs.</summary>
        public bool Open(string name)
        {
            string lump = name.Trim().ToUpperInvariant();
            if (lump.Length == 0) return false;

            int res = Wad.FindRes(lump);
            if (res < 0)
            {
                Status = "карта " + lump + " не найдена";
                return false;
            }

            Load(MapFile.Read(Wad.LoadRes(res), lump), lump, "");
            return true;
        }

        /// <summary>
        /// F5: launches the game on this map, saved to a temporary WAD.
        ///
        /// An editor that cannot show the level in motion makes every change a
        /// guess; the original had the same idea with its own play key.
        /// </summary>
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
                if (game == null)
                {
                    Status = "dptdoom2d.exe не найден рядом";
                    return;
                }

                var start = new System.Diagnostics.ProcessStartInfo();
                start.FileName = game;
                start.Arguments = "-file \"" + temp + "\" -warp 1";
                start.UseShellExecute = false;
                start.WorkingDirectory = Path.GetDirectoryName(game);

                System.Diagnostics.Process.Start(start);
                Status = "игра запущена на этой карте";
            }
            catch (Exception e)
            {
                Status = "ошибка запуска: " + Log.Describe(e);
            }
        }

        // ------------------------------------------------------------------ view

        public void CenterOn(int mapX, int mapY)
        {
            CamX = mapX - ViewW / 2;
            CamY = mapY - ViewH / 2;
            ClampCamera();
        }

        private void ClampCamera()
        {
            int maxX = MapFile.Width * MapCanvas.CellSize - ViewW;
            int maxY = MapFile.Height * MapCanvas.CellSize - ViewH;

            if (CamX > maxX) CamX = maxX;
            if (CamY > maxY) CamY = maxY;
            if (CamX < 0) CamX = 0;
            if (CamY < 0) CamY = 0;
        }

        private void ClampPalette()
        {
            int count = PaletteCount();
            int visible = TilesPerRow * (PaletteH / Tile);

            int max = count - visible;
            if (max < 0) max = 0;

            if (PaletteScroll > max) PaletteScroll = max;
            if (PaletteScroll < 0) PaletteScroll = 0;
        }

        public int PaletteCount()
        {
            if (Tool == EditorTool.Things) return things.Count;
            if (Tool == EditorTool.Type) return CellValues.Length;
            return wallIndices.Count;
        }

        private void UpdateStatus()
        {
            if (Map == null) return;

            if (hoverX < 0)
            {
                Status = Map.Summary();
                return;
            }

            int at = hoverY * MapFile.Width + hoverX;

            Status = string.Format("{0},{1}  стена {2}  тип {3}",
                hoverX, hoverY, Map.Back[at], Map.Type[at]);
        }

        private static bool InView(int x, int y)
        {
            return x >= ViewX && x < ViewX + ViewW && y >= ViewY && y < ViewY + ViewH;
        }

        private static bool InPalette(int x, int y)
        {
            return x >= PaletteX && x < PaletteX + PaletteW
                && y >= PaletteY && y < PaletteY + PaletteH;
        }

        // ------------------------------------------------------------------ draw

        /// <summary>Draws the framebuffer part: the map and the palette pictures.</summary>
        public void Draw(Win32Window window)
        {
            Vga.SetRect(0, ScreenW, 0, ScreenH);
            Vga.Clr(0, ScreenW, 0, ScreenH, 0);

            if (Map != null)
            {
                MapCanvas.Draw(Map, CamX, CamY, ViewX, ViewY, ViewW, ViewH,
                    ShowBack, ShowFront, ShowTypes);
            }

            DrawPalette();
        }

        private void DrawPalette()
        {
            // The strip has its own background so the map does not bleed into it.
            // Index 0 is black in the Doom palette; 0x10 would be pink.
            Vga.Clr(PaletteX, PaletteW, PaletteY, PaletteH, 0);

            if (Map == null) return;

            // Cell types are a named list, not pictures: the UI draws those, because
            // a coloured square with no label tells a mapper nothing.
            if (Tool == EditorTool.Type) return;

            int count = PaletteCount();
            int visible = TilesPerRow * (PaletteH / Tile);

            for (int i = 0; i < visible; ++i)
            {
                int index = PaletteScroll + i;
                if (index >= count) break;

                int x = PaletteX + (i % TilesPerRow) * Tile;
                int y = PaletteY + (i / TilesPerRow) * Tile;

                if (Tool == EditorTool.Things)
                {
                    VgaImage sprite = MapCanvas.ThingSprite(things[index].Type);
                    if (sprite != null) Vga.Spr(x + 1, y + 1, sprite);
                }
                else
                {
                    int wall = wallIndices[index];
                    int id = Wad.FindRes(Map.WallName[wall]);
                    if (id >= 0)
                    {
                        VgaImage image = Res.Img(id);
                        if (image != null) Vga.Pic(x + 1, y + 1, image);
                    }
                }
            }
        }

        /// <summary>Index into the palette of the selected entry, or -1.</summary>
        public int SelectedPaletteIndex
        {
            get
            {
                if (Tool == EditorTool.Things)
                {
                    for (int i = 0; i < things.Count; ++i)
                        if (things[i].Type == ThingBrush) return i;
                    return -1;
                }

                if (Tool == EditorTool.Type)
                {
                    for (int i = 0; i < CellValues.Length; ++i)
                        if (CellValues[i] == CellBrush) return i;
                    return -1;
                }

                for (int i = 0; i < wallIndices.Count; ++i)
                    if (wallIndices[i] == Brush) return i;

                return -1;
            }
        }

        // ---- data the UI overlay needs ------------------------------------

        public IList<int> WallIndices { get { return wallIndices; } }
        public IList<ThingChoice> Things { get { return things; } }

        public string CurrentWallName
        {
            get
            {
                if (Map == null || Brush <= 0 || Brush >= 256) return "";
                return Map.WallName[Brush];
            }
        }

        public string ToolName
        {
            get
            {
                switch (Tool)
                {
                    case EditorTool.Back: return "задний слой";
                    case EditorTool.Front: return "передний слой";
                    case EditorTool.Type: return "типы клеток";
                    default: return "объекты";
                }
            }
        }

        public string CurrentThingName
        {
            get
            {
                for (int i = 0; i < things.Count; ++i)
                    if (things[i].Type == ThingBrush) return things[i].Name;
                return "";
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

        /// <summary>True while a name is being typed, so the UI can show the prompt.</summary>
        public bool PromptActive { get { return prompt != null; } }
        public string PromptLabel { get { return prompt == null ? "" : prompt.Label; } }
        public string PromptValue { get { return prompt == null ? "" : prompt.Value; } }

        // ------------------------------------------------------------------ help

        public static readonly string[] HelpLines =
        {
            "Редактор карт DPT.DOOM 2D",
            "",
            "Карта рисуется настоящими текстурами из WAD,",
            "как в DOS-редакторе 1996 года.",
            "",
            "Левая кнопка  — рисовать",
            "Правая       — стирать",
            "Средняя      — таскать карту",
            "Колесо       — прокрутка",
            "",
            "1-4  инструмент: задний / передний / типы / объекты",
            "B F T — показать слои,  стрелки — камера",
            "Ctrl+S сохранить,  Ctrl+O открыть",
            "F5 — запустить игру на этой карте",
            "Esc — выход",
            "",
            "Любая клавиша — закрыть"
        };
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
    /// A one-line text prompt drawn on the framebuffer.
    ///
    /// The editor has no dialog boxes (it is not a Windows application), so a file
    /// name is typed straight into the screen, the way the original's inputbox
    /// worked.
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
        public const int Return = 13;
        public const int Escape = 27;
        public const int Left = 37;
        public const int Up = 38;
        public const int Right = 39;
        public const int Down = 40;

        public const int D1 = 0x31, D2 = 0x32, D3 = 0x33, D4 = 0x34;
        public const int B = 0x42, F = 0x46, T = 0x54;
        public const int O = 0x4F, S = 0x53;

        public const int F1 = 0x70, F5 = 0x74;

        /// <summary>Modifier bits reported by <see cref="Win32Window.Modifiers"/>.</summary>
        public const int ShiftMask = 1, CtrlMask = 2, AltMask = 4;

        /// <summary>VK to the character it types, for file names. 0 for anything else.</summary>
        public static char ToChar(int key)
        {
            if (key >= 0x30 && key <= 0x39) return (char)key;
            if (key >= 0x41 && key <= 0x5A) return (char)('a' + (key - 0x41));

            switch (key)
            {
                case 0xBE: return '.';     // OEM period
                case 0xBF: return '/';     // OEM slash
                case 0xBD: return '-';     // OEM minus
                case 0xDB: return '[';
                case 0xDD: return ']';
                default: return '\0';
            }
        }
    }
}
