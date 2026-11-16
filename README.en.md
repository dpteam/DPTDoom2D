# Doom 2D

XNA/FNA port of the 1996 Doom2D engine by Prikol Software. Gameplay is a
one-to-one port; the renderer is still the original 8-bit palette software
renderer, so the picture matches the DOS game pixel for pixel.

```
Doom2D/                 the game (C#, net40, x64)
  Engine/               platform layer: WAD, framebuffer, palette, input, sound
  Game/                 gameplay: world, player, monsters, weapons, menu
  tools/                build, packaging, icon and docs-image generation
external/fna/           FNA (Ms-PL) sources + native DLLs
docs/                   documentation and screenshots (Russian)
refs/                   upstream Doom2D sources (not committed)
```

## Build

```powershell
./Doom2D/tools/fetch-data.ps1     # doom2d.wad + music from upstream
./Doom2D/tools/build.ps1 -Run     # build and start
```

Requires the .NET SDK (9.0+) and Windows x64. FNA sources and native libraries
are vendored, so nothing else is downloaded.

```powershell
./Doom2D/tools/build.ps1 -Test    # headless checks: 19 maps, saves, front end
./Doom2D/tools/build.ps1 -Pack    # dist/Doom2D/, portable
```

## Documentation

Russian, in `docs/`:

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — how the port works
- [docs/FORMATS.md](docs/FORMATS.md) — data formats
- [docs/BUILDING.md](docs/BUILDING.md) — build, CI, checks
- [docs/CONTROLS.md](docs/CONTROLS.md) — keys and cheats
- [docs/ROADMAP.md](docs/ROADMAP.md) — remaining work
- [docs/PORTING.md](docs/PORTING.md) — port log

## License

GPLv2 — see [LICENSE](LICENSE). The port is a derivative of Doom2D:Rembo
(GPLv2). Game data is fetched from upstream and is not part of this repository.
FNA is Microsoft Public License — see [external/fna/LICENSE](external/fna/LICENSE).
