# Doom2D → XNA (FNA) порт

Цель: **геймплей 1:1 Doom2D**, движок — XNA/FNA из TerrariaXDK.
Ничего из Terraria (мир, тайлы, NPC, инвентарь) не используется — только
платформенный слой: `Microsoft.Xna.Framework*` (FNA), SDL2, FAudio.

## Что лежит в папке

| Путь | Что это |
|---|---|
| `doom2d-rembo/` | Эталонные исходники Doom2D (C, GPLv2), клон https://github.com/Doom2D/doom2d-rembo |
| `TerrariaXDK/` | Донор движка: Terraria X360 декомпил + вендоренный FNA (`Source/Microsoft.Xna.Framework*`) |
| `Doom2D/` | Наш порт (C# / .NET Framework 4.0 / x64, как и TerrariaXDK) |

## Ключевое решение: рендер

Оригинал рисует в **8-битный палитровый фреймбуфер** (`scrbuf[64000]`,
320x200) функциями `V_spr` / `V_clr` / `V_pic` / `V_sprf`, плюс:
- `clrmap` (12 карт затемнения) — затемнение по расстоянию/свету,
- `mixmap` — «дым»,
- `flametab` — пламя,
- одноцветная заливка силуэта (`V_spr1color`, тени),
- замена цветов 0x70..0x7F по цвету игрока.

Поэтому порт **сохраняет программный 8-битный рендер**, а XNA отвечает за:
1. вывод кадра: `byte[] SCRW*SCRH` → `Color[]` через палитру → `Texture2D.SetData` → `SpriteBatch` с `PointClamp` (масштаб целочисленный),
2. окно, VSync, ввод, звук.

Это даёт пиксель-в-пиксель совпадение с оригиналом и позволяет переносить
`vga.c`/`view.c`/`miscc.c` почти построчно. Позже, если понадобится,
кадр можно рисовать спрайтами — но это уже смена картинки, а не геймплея.

## Соответствие модулей

| C (rembo) | C# (порт) | Примечание |
|---|---|---|
| `vga.c` | `Engine/Vga.cs` | фреймбуфер, putpixel/getpixel, клип, палитра |
| `files.c` | `Engine/Wad.cs` | WAD-каталог, ресурсы, карты, сейвы |
| `memory.c` | `Engine/ResCache.cs` | `M_lock`/`M_unlock` |
| `sound.c` | `Engine/Sound.cs` | DMI 8-bit → PCM16 → `SoundEffectInstance` |
| `music.c` | `Engine/Music.cs` | DMM-модуль (сэмплы + паттерны) |
| `keyb.c` | `Engine/Keyboard.cs` | `KeyboardState`, `keys[]` |
| `config.c` | `Engine/Config.cs` | `default.cfg` / аргументы |
| `view.c` | `Game/View.cs` | карта, стены, небо, лава |
| `game.c` | `Game/Game.cs` | состояния, переходы, HUD |
| `player.c` | `Game/Player.cs` | |
| `monster.c` | `Game/Monster.cs` | |
| `weapons.c` | `Game/Weapons.cs` | |
| `items.c` | `Game/Items.cs` | |
| `switch.c` | `Game/Switch.cs` | |
| `dots.c` | `Game/Dots.cs` | кровь/искры |
| `smoke.c` | `Game/Smoke.cs` | дым/пламя |
| `fx.c` | `Game/Fx.cs` | телепорт, пузыри, переходы |
| `bmap.c` | `Game/Bmap.cs` | блочная карта |
| `menu.c` | `Game/Menu.cs` | меню, читы |
| `miscc.c` | `Game/Misc.cs` | физика `Z_*`, HUD-шрифт |
| `a8.c` | `Game/A8.cs` | внутриигровые видео (в оригинале закомментированы, не портировались) |
| `main.c` | `Program.cs` + `Doom2DGame.cs` | инициализация, цикл 20 Гц |

Дополнительно (не из C-кода, для проверки и удобства):

| Файл | Зачем |
|---|---|
| `Headless.cs` | прогон настоящего игрового цикла без окна (`--simulate`, `--shell`, `--saveload`, `--levelend`) |
| `Engine/Screen.cs` | фреймбуфер → `Texture2D` + BMP для скриншотов |
| `Fna/TerrariaLogShim.cs` | заглушка `Terraria.Program.LogWarn`, которую зовёт `SpriteBatch` донора |

## Форматы данных (без изменений)

- **WAD**: `IWAD`/`PWAD`, каталог `{int ofs,int len,char name[8]}`.
- **Спрайт/картинка** (`D_START..D_END`, `S_START..S_END`): `ushort w,h; short sx,sy;` затем `w*h` байт индексов палитры (0 = прозрачный).
- **Звук (DMI)**: `uint len,rate,lstart,llen` затем 8-битные знаковые сэмплы.
- **Карта**: `"Doom2D\x1A"` + блоки `{short t,st; int sz}`; блоки `MB_BACK/WTYPE/FRONT/THING/SWITCH2/MUSIC/SKY`.
- **Сейвы**: `savgame0.dat`, заголовок 24 байта + `ver=3` + блоки подсистем.

Всё читается теми же структурами (`[StructLayout(Pack=1)]`), чтобы карты
и сейвы Doom2D открывались как есть.

## Этапы

1. **Каркас**: проект `Doom2D.csproj` (net40/x64), окно FNA, WAD-загрузчик, палитра, фреймбуфер, вывод кадра. ✔
2. **Ввод + меню**: `keyb.c` + `menu.c`, навигация по меню, читы, сейв-слоты. ✔
3. **Мир**: `view.c` + `bmap.c` + загрузка карты, небо, вода, лифты, двери. ✔
4. **Игрок и оружие**: `player.c`, `weapons.c`, `items.c`, `switch.c`. ✔
5. **Монстры**: `monster.c`, `miscc.c` (урон, взрывы, `Z_*`). ✔
6. **Эффекты**: `dots.c`, `smoke.c`, `fx.c`. ✔
7. **Звук**: `sound.c` (DMI → `SoundEffect`). ✔
8. **Сейвы, интермиссия, финал, 2 игрока**: ✔ (split-screen и deathmatch на месте).
9. **Музыка**: `music.c` — состояние портировано, XM-плеер нет. ✖

## Проверка

Порт проверяется headless-прогонами настоящего игрового цикла
(`Doom2D.exe --simulate/--shell/--saveload/--levelend`), кадр сбрасывается в BMP.
Так прогоняются все 19 карт, меню, сохранения и выход с уровня — без окна и GPU.

## Лицензия

Doom2D:Rembo — GPLv2. Порт — производная работа, значит **GPLv2**.
Исходники Doom2D открыты самими авторами (Prikol Software), `COPYING` в
`doom2d-rembo/`. TerrariaXDK — декомпил, его код берём только как
платформенный слой FNA (сам FNA — Ms-PL).
