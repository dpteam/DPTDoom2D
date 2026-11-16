# Doom 2D — XNA/FNA порт

Геймплей **Doom2D** (Prikol Software, 1996) один-в-один, движок — XNA/FNA
из [TerrariaXDK](../TerrariaXDK). Ничего от Terraria не используется: только
платформенный слой (`Microsoft.Xna.Framework`, SDL2, FAudio).

```
Doom2D.exe                 игра
Data/doom2d.wad            все ресурсы (спрайты, звуки, уровни)
Data/Music/                музыкальные треки (XM)
tools/build.ps1            сборка + копирование нативных FNA-DLL
tools/screenshot.ps1       снимок окна (для проверки рендера)
tools/make-icon.ps1        генерация app.ico из титульного экрана
```

## Сборка

Нужен .NET SDK и донор движка рядом:

```
XNADoom2D/
├─ Doom2D/           этот проект
├─ doom2d-rembo/     исходники оригинала (GPLv2) + данные
└─ TerrariaXDK/      донор: Source/ + нативные SDL2/FNA3D/FAudio DLL
```

```powershell
cd Doom2D
tools\build.ps1            # build + нативные DLL
tools\build.ps1 -Run       # ... и запуск
```

`FNA`-исходники **не копируются**, а линкуются из `..\TerrariaXDK\Source`
(`<Compile Include=... Link=...>`), поэтому донор остаётся нетронутым.
Нативные `SDL2.dll`, `FNA3D.dll`, `FAudio.dll`, `libtheorafile.dll` берутся
оттуда же (`TXDK_NATIVE_DIR` переопределяет путь).

## Что уже работает

| Модуль | Состояние |
|---|---|
| WAD-загрузчик, палитра, gamma | ✔ |
| 8-битный программный рендер (спрайты, клип, colormap-затенение, MIXMAP-дым, пламя) | ✔ |
| Окно XNA, integer scaling, PointClamp | ✔ |
| Титульный экран, меню, все подменю | ✔ |
| Новая игра, загрузка/сохранение (7 слотов), смерть/респаун | ✔ |
| Карты 1..19, небо, вода, лава, лифты, двери, ловушки, секреты | ✔ |
| Игрок: бег, прыжок, плавание, 11 видов оружия, броня, ключи, powerup'ы | ✔ |
| Монстры: 20 видов, вся логика AI/стрельбы/смерти | ✔ |
| Эффекты: кровь, искры, дым, пламя, туман телепорта, пузыри | ✔ |
| Интермиссия, переходы между уровнями | ✔ |
| Два игрока (split-screen, кооп и deathmatch) | ✔ |
| Звук (DMI → SoundEffect) | ✔ |
| Музыка (XM) | ✖ пока тишина, см. ниже |

## Проверка без окна

Игра умеет прогонять настоящий игровой цикл headless — это же и есть тесты:

```powershell
Doom2D.exe --simulate 1000 out.bmp 1 "5:right,50:fire"   # N тиков на карте, дамп кадра
Doom2D.exe --simulate 120 out.bmp 1 "" 2p                # split-screen
Doom2D.exe --shell 140 out.bmp "5:space,20:enter,40:space"  # титул → меню → новая игра
Doom2D.exe --saveload 150                                # save/load, сверка состояния
Doom2D.exe --levelend 1                                  # выход с уровня → интермиссия
Doom2D.exe --switches 1                                  # все триггеры карты
Doom2D.exe --walls 2                                     # какие стены реально используются
Doom2D.exe --dump TITLEPIC title.bmp                     # один ресурс WAD в BMP
Doom2D.exe --list                                        # каталог WAD
```

Клавиши в скрипте: `left right up down fire jump use next prev space enter esc`,
плюс `tick:-key` для отпускания.

## Музыка

Треки — FastTracker 2 (XM), оригинал играл их через SDL_mixer/mikmod.
Replay-движок не портирован, поэтому `Engine/Music.cs` держит корректным всё
состояние (загрузка/старт/стоп/громкость/смена по времени), но молчит.
Портировать = написать XM-плеер (паттерны + эффекты + сведение сэмплов) поверх
`DynamicSoundEffectInstance`.

## Лицензия

Doom2D:Rembo — **GPLv2** (`../doom2d-rembo/COPYING`), порт — производная
работа, значит тоже GPLv2. TerrariaXDK — декомпил Terraria, используется
только как носитель FNA (сам FNA — Ms-PL).
