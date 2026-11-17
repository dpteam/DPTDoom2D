# Сборка

## Что нужно

- Windows x64 (сама игра — AnyCPU и запускается на x86 и x64)
- [.NET SDK](https://dotnet.microsoft.com/download) 9.0 или новее — только для
  сборки; запуск идёт на .NET Framework 4.0, который есть в Windows с 8-й версии
  (и ставится отдельно на XP)
- Больше ничего: сторонних библиотек нет вообще

## Данные игры

`doom2d.wad` (4.4 МБ) и музыка (1.7 МБ) в git не лежат — это GPL-ассеты,
которые уже есть в upstream. Один раз:

```powershell
./DPTDOOM2D/tools/fetch-data.ps1
```

Скрипт клонирует `github.com/Doom2D/doom2d-rembo` в `refs/doom2d-rembo`
(папка в `.gitignore`) и обновляет её при повторном запуске. `-Force`
переклонирует с нуля.

## Сборка

```powershell
cd DPTDOOM2D
./tools/build.ps1              # сборка + проверки без окна
./tools/build.ps1 -SkipTests   # только сборка
./tools/build.ps1 -Net         # + проверки сети и релея
./tools/build.ps1 -Pack        # + dist/DPTDOOM2D/, готовый к переносу
./tools/build.ps1 -Run         # + запуск
```

Или напрямую через решение:

```powershell
dotnet build DPTDOOM2D.slnx -c Release
```

Решение собирает четыре проекта:

| Проект | Результат |
|---|---|
| `DPTDOOM2D` | `dptdoom2d.exe` — игра |
| `DPTDOOM2D.Launcher` | `dptdoom2d-launcher.exe` — лаунчер |
| `DPTDOOM2D.Master` | `dptdoom2d-master.exe` — мастер + релей |
| `DPTDOOM2D.Shared` | `DPTDOOM2D.Shared.dll` — протокол (в игру вкомпилирован) |

Игра — **один exe**: протокол мастера не подключается как DLL, а компилируется
внутрь (`<Compile Include="..\DPTDOOM2D.Shared\...">`), чтобы рядом с
`dptdoom2d.exe` не лежало ничего лишнего.

### Что делает `-Pack`

```
dist/DPTDOOM2D/
├─ dptdoom2d.exe             игра
├─ dptdoom2d.exe.config
├─ dptdoom2d-launcher.exe    лаунчер
├─ dptdoom2d-master.exe      мастер + релей
├─ DPTDOOM2D.Shared.dll
├─ Data/
│  ├─ doom2d.wad             базовые данные
│  ├─ default.cfg            привязки и настройки
│  ├─ Music/                 30 XM-треков
│  ├─ Maps/                  166 паков кастомных карт
│  └─ Mods/                  сюда кладутся моды (WAD, PK3)
├─ Storage/                  создаётся при первом запуске
│  ├─ Logs/                  логи игры
│  └─ webview/               страница лаунчера
├─ README.md
├─ LICENSE
└─ CHANGELOG.md
```

Папку можно копировать на любую Windows-машину с .NET Framework 4.0 — установка
не нужна. Логи пишутся в `Storage/Logs` рядом с exe.

## Проверки без окна

Вся игра умеет работать без окна и без GPU — это и есть тесты.

```powershell
dptdoom2d.exe --simulate <тиков> <out.bmp> [карта] [клавиши] [2p|dm]
dptdoom2d.exe --shell <тиков> <out.bmp> [клавиши]
dptdoom2d.exe --saveload <тиков>
dptdoom2d.exe --levelend <карта>
dptdoom2d.exe --switches <карта>
dptdoom2d.exe --walls <карта>
dptdoom2d.exe --music <трек>
dptdoom2d.exe --sounds
dptdoom2d.exe --cheats
dptdoom2d.exe --maps
dptdoom2d.exe --modbuild <out.pk3> <dir>...
dptdoom2d.exe --modlist <file.pk3>
dptdoom2d.exe --modunpack <file.pk3> <dir>
dptdoom2d.exe --proto-list
dptdoom2d.exe --proto <id>
dptdoom2d.exe --wadbuild <list.lst>
dptdoom2d.exe --wadbuild <out.wad> <file>...
dptdoom2d.exe --wadlist <file.wad>
dptdoom2d.exe --dump <ресурс> <out.bmp>
dptdoom2d.exe --list
```

Скрипт клавиш: `тик:клавиша`, через запятую; `тик:-клавиша` отпускает клавишу.
Имена: `left right up down fire jump use next prev space enter esc y n`
(это привязки по умолчанию из `main.c`, не стрелки).

Пример: пройти вправо, выстрелить на 40-м тике и посмотреть кадр на 300-м.

```powershell
dptdoom2d.exe --simulate 300 out.bmp 1 "5:right,40:fire,44:-fire"
```

`--saveload` играет, сохраняет, обнуляет мир, загружает и сверяет координаты,
патроны, жизни и фраги. Возвращает код 1 при расхождении — годится для CI.

`--sounds` разбирает все 140 лампов `DS*` и проверяет, что каждый дал данные;
`--music` грузит трек так же, как игра (файл, потом DMM из WAD), и рендерит
секунду — если плеер молчит, peak равен нулю и код возврата 1. Это те две
проверки, которые ловят «звука нет» и «музыка молчит».

`--maps` печатает библиотеку паков и уровни в текущем наборе WAD.

`--cheats` прогоняет все девять кодов оригинала и проверяет эффект каждого.
Кириллические коды — это те же клавиши, поэтому проверка покрывает обе раскладки.

`--modbuild` / `--modlist` / `--modunpack` — модкит: собрать PK3 из папки,
посмотреть, что из него загрузится, распаковать обратно.

`--proto-list` и `--proto <id>` — витрина прототипов (см. [PROTOTYPES.md](PROTOTYPES.md)).
`--wadbuild` / `--wadlist` — сборщик WAD из `DOOMWAD/MAKEWAD.C`, перенесённый
как инструмент: собирает WAD из списка файлов и печатает каталог.

Все headless-режимы пересеивают ГПСЧ фиксированным зерном (`Headless.Seed`),
поэтому одна и та же команда всегда даёт один и тот же кадр. На этом держатся и
проверки в CI, и скриншоты в `docs/images`: их можно перегенерировать и увидеть
настоящий diff, если что-то изменилось.

## Проверки сети

```powershell
./tools/net-test.ps1      # хост + гость по 127.0.0.1
./tools/relay-test.ps1    # мастер + релей + хост + гость
```

`net-test` поднимает две копии игры, ждёт handshake и проверяет, что за
несколько секунд не появилось `DESYNC`. `relay-test` дополнительно поднимает
мастер и релей, регистрирует хоста, читает id сессии из лога и подключает гостя
через релей — то есть проверяет путь, которым играют через интернет.

## CI

`.github/workflows/build.yml`, Windows-раннер:

1. `fetch-data.ps1` — данные из upstream
2. `dotnet build DPTDOOM2D.slnx` — все четыре проекта
3. `build.ps1` — 19 карт, сохранения, фронтенд, 140 звуков, 30 треков (из файлов
   и из WAD), 166 паков карт, 9 читов, модкит, прототипы
4. `build.ps1 -Net` — сеть и релей
5. `build.ps1 -Pack` → артефакт **DPTDOOM2D-win64**
6. `make-docs-images.ps1` → артефакт **docs-images**

## Скрипты

| Скрипт | Зачем |
|---|---|
| `tools/build.ps1` | сборка, проверки, упаковка, запуск |
| `tools/fetch-data.ps1` | клонировать/обновить `refs/doom2d-rembo` |
| `tools/make-icon.ps1` | `resources/dptdoom2d.ico` — квадратный кроп 1:1 из титульника (16…256 px) |
| `tools/make-proto-assets.ps1` | пересобрать `Prototypes/ProtoAssets.cs` из `.VGA` (base64) |
| `tools/make-docs-images.ps1` | картинки для доков из реального фреймбуфера + оверлей и лаунчер |
| `tools/screenshot.ps1` | снять клиентскую область окна, опционально «нажав» клавиши |
| `tools/net-test.ps1` | проверка прямой сетевой сессии |
| `tools/relay-test.ps1` | проверка сессии через мастер и релей |
