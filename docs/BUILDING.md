# Сборка

## Что нужно

- Windows x64
- [.NET SDK](https://dotnet.microsoft.com/download) 8.0 или новее
  (собирает проект под `net40` через reference assemblies из NuGet)
- Больше ничего: FNA-исходники и нативные DLL уже в репозитории

## Данные игры

`doom2d.wad` (4.4 МБ) и музыка (1.7 МБ) в git не лежат — это GPL-ассеты,
которые уже есть в upstream. Один раз:

```powershell
./Doom2D/tools/fetch-data.ps1
```

Скрипт клонирует `github.com/Doom2D/doom2d-rembo` в `refs/doom2d-rembo`
(папка в `.gitignore`) и обновляет её при повторном запуске. `-Force`
переклонирует с нуля.

## Сборка

```powershell
cd Doom2D
./tools/build.ps1              # сборка + нативные DLL рядом с exe
./tools/build.ps1 -Test        # ... + проверки без окна
./tools/build.ps1 -Run         # ... + запуск
./tools/build.ps1 -Pack        # ... + dist/Doom2D/, готовый к переносу
```

Или напрямую:

```powershell
dotnet build Doom2D/Doom2D.csproj -c Release
```

Результат: `Doom2D/bin/Release/net40/Doom2D.exe`.

### Что делает `-Pack`

Собирает `dist/Doom2D/`:

```
Doom2D.exe
Doom2D.exe.config
SDL2.dll  FNA3D.dll  FAudio.dll  libtheorafile.dll
Data/doom2d.wad  Data/default.cfg  Data/Music/*.xm-подобные
README.md
```

Папку можно копировать на любую Windows-машину с .NET Framework 4.0
(он есть в Windows 8 и новее) — установка не нужна.

## Нативные библиотеки

`tools/build.ps1` ищет их по порядку:

1. `$env:TXDK_NATIVE_DIR`
2. `external/fna/native/win64` — то, что лежит в репозитории
3. `refs/TerrariaXDK` — донор движка, если он есть

Без `SDL2.dll` падает статический конструктор FNA с невнятным
`DllNotFoundException`, поэтому скрипт проверяет это заранее.

## Проверки без окна

Вся игра умеет работать без окна и GPU — это и есть тесты.

```powershell
Doom2D.exe --simulate <тиков> <out.bmp> [карта] [клавиши] [2p|dm]
Doom2D.exe --shell <тиков> <out.bmp> [клавиши]
Doom2D.exe --saveload <тиков>
Doom2D.exe --levelend <карта>
Doom2D.exe --switches <карта>
Doom2D.exe --walls <карта>
Doom2D.exe --dump <ресурс> <out.bmp>
Doom2D.exe --list
```

Скрипт клавиш: `тик:клавиша`, через запятую; `тик:-клавиша` отпускает клавишу.
Имена: `left right up down fire jump use next prev space enter esc y n`
(это привязки по умолчанию из `main.c`, не стрелки).

Пример: пройти вправо, выстрелить на 40-м тике и посмотреть кадр на 300-м.

```powershell
Doom2D.exe --simulate 300 out.bmp 1 "5:right,40:fire,44:-fire"
```

`--saveload` играет, сохраняет, обнуляет мир, загружает и сверяет координаты,
патроны, жизни и фраги. Возвращает код 1 при расхождении — годится для CI.

## CI

`.github/workflows/build.yml`, Windows-раннер:

1. `fetch-data.ps1` — данные из upstream
2. `dotnet build`
3. `build.ps1 -Test` — 19 карт, сохранения, фронтенд
4. `build.ps1 -Pack` → артефакт **Doom2D-win64**
5. `make-docs-images.ps1` → артефакт **docs-images** (кадры текущей сборки)

## Скрипты

| Скрипт | Зачем |
|---|---|
| `tools/build.ps1` | сборка, нативные DLL, проверки, упаковка, запуск |
| `tools/fetch-data.ps1` | клонировать/обновить `refs/doom2d-rembo` |
| `tools/make-icon.ps1` | `app.ico` из титульного экрана (16…256 px) |
| `tools/make-docs-images.ps1` | картинки для доков из реального фреймбуфера |
| `tools/screenshot.ps1` | снять окно игры в PNG (для проверки рендера вручную) |
