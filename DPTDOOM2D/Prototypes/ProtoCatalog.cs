//
// DPT.DOOM 2D — the prototype showcase catalogue.
// Copyright (C) 2026 DartPower Team. GPLv2.
//

using System;
using System.Collections.Generic;

namespace DPTDOOM2D.Prototypes
{
    /// <summary>
    /// The prototypes from refs/D2D_Also that have been ported to C#.
    ///
    /// Every entry names the directory its DOS original lived in, so the
    /// showcase is also an index of the archive: what is here runs, what is not
    /// here is still only C or assembler (see docs/ROADMAP.md).
    /// </summary>
    internal static class ProtoCatalog
    {
        internal sealed class Item
        {
            public string Id;
            public string Title;
            public string Source;
            public string Description;
            public bool Visual;
            public Func<ProtoHost.Prototype> Create;
        }

        private static readonly Item[] items = {
            new Item {
                Id = "truba", Title = "TRUBA — smoke tunnel",
                Source = "oldprog/watcom/PROG/TRUBA",
                Description = "Дым в трубе: 2000 частиц, спрайты SMOKE.VGA. Отсюда вырос smoke.c в Doom2D.",
                Visual = true,
                Create = delegate { return new TrubaDemo(); }
            },
            new Item {
                Id = "planet", Title = "PLANET — world generator",
                Source = "oldprog/watcom/PROG/PLANET",
                Description = "Генератор планеты: континенты, климат, ветер, осадки. 256x256, замыкается по краям.",
                Visual = true,
                Create = delegate { return new PlanetDemo(); }
            },
            new Item {
                Id = "landgen", Title = "LANDGEN — fractal terrain",
                Source = "oldprog/watcom/PROG/TEST",
                Description = "Фрактальный ландшафт: 256x256 высот из value-шума, холмы/скалы/лес/лёд отдельными слоями.",
                Visual = true,
                Create = delegate { return new LandgenDemo(); }
            },
            new Item {
                Id = "escapade", Title = "ESCAPADE — platformer",
                Source = "oldprog/TC/PROGS/ESCAPADE",
                Description = "Платформер 1995-го: поле 64x64, герой с анимациями, спрайты HERO1.VGA. Прямой предок Doom2D.",
                Visual = true,
                Create = delegate { return new EscapadeDemo(); }
            },
            new Item {
                Id = "vgamaze", Title = "VGA MAZE — tile field",
                Source = "oldprog/TC/PROGS/VGAMAZE",
                Description = "Лабиринт из случайных тайлов 64x64 со скроллом. Первая программа в архиве с формой будущего Doom2D.",
                Visual = true,
                Create = delegate { return new VgaMazeDemo(); }
            },
            new Item {
                Id = "doomwad", Title = "DOOM WAD MAKER — tool",
                Source = "oldprog/TC/PROGS/DOOMWAD",
                Description = "Сборщик WAD из списка файлов (MAKEWAD.C, 1995). Консольный: --wadbuild, --wadlist.",
                Visual = false,
                Create = null
            }
        };

        public static IList<Item> Items { get { return items; } }

        public static Item Find(string id)
        {
            for (int i = 0; i < items.Length; ++i)
                if (string.Equals(items[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return items[i];

            return null;
        }

        /// <summary>
        /// Machine-readable listing for the launcher: one prototype per line,
        /// fields separated by tabs (id, title, source, description, visual).
        /// </summary>
        public static string Encode()
        {
            var text = new System.Text.StringBuilder();

            for (int i = 0; i < items.Length; ++i)
            {
                Item item = items[i];

                if (text.Length > 0) text.Append('\n');

                text.Append(item.Id).Append('\t');
                text.Append(item.Title).Append('\t');
                text.Append(item.Source).Append('\t');
                text.Append(item.Description).Append('\t');
                text.Append(item.Visual ? "1" : "0");
            }

            return text.ToString();
        }

        /// <summary>--proto-list: what the showcase contains.</summary>
        public static int List()
        {
            Console.WriteLine("Прототипы (запуск: --proto <id>):");

            for (int i = 0; i < items.Length; ++i)
            {
                Item item = items[i];
                Console.WriteLine(string.Format("  {0,-10} {1}", item.Id, item.Title));
                Console.WriteLine(string.Format("             {0}", item.Source));
                Console.WriteLine(string.Format("             {0}", item.Description));
            }

            return 0;
        }
    }
}
