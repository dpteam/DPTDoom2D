# DPT.DOOM 2D

[Русский](README.md) | **English**

Native Win32 port of the 1996 Doom2D engine by Prikol Software. Gameplay is a
one-to-one port; the renderer is still the original 8-bit palette software
renderer, so the picture matches the DOS game pixel for pixel.

**No third-party libraries.** The window is `user32`, the frame is `gdi32`, the
sound is `winmm`. No SDL, no OpenGL, no FNA: `dptdoom2d.exe` runs on Windows XP
SP3 and on Windows 11 without installing anything.

```
DPTDOOM2D/                 the game (C#, net40)
  Engine/                  platform: WAD, framebuffer, palette, input, config, logs
  Audio/                   own mixer, waveOut output, XM replayer, DMM -> XM
  Win32/                   window, DIB blitter, Win32 declarations
  Game/                    gameplay: world, player, monsters, weapons, menu
  Net/                     two-player lockstep, protocol, game-loop bridge
  Ui/                      the F2 overlay
  Maps/                    custom map library (166 WADs)
  tools/                   build, packaging, icon, docs images, network tests
DPTDOOM2D.Shared/          master protocol (shared by the game and the server)
DPTDOOM2D.Master/          session directory + relay
DPTDOOM2D.Launcher/        WinForms launcher
docs/                      documentation and screenshots (Russian)
refs/                      upstream Doom2D sources (not committed)
```

## Build

```powershell
./DPTDOOM2D/tools/fetch-data.ps1     # doom2d.wad + music from upstream
./DPTDOOM2D/tools/build.ps1 -Run     # build and start
```

Requires the .NET SDK (9.0+) to build and Windows to run. Nothing else is
downloaded.

```powershell
./DPTDOOM2D/tools/build.ps1          # headless checks: 19 maps, 166 map packs, sounds, 30 tracks
./DPTDOOM2D/tools/build.ps1 -Net     # ... + direct and relayed network sessions
./DPTDOOM2D/tools/build.ps1 -Pack    # dist/DPTDOOM2D/, portable
```

## Multiplayer

Direct (LAN):

```powershell
dptdoom2d.exe -net host -port 5041 -map 1
dptdoom2d.exe -net join -address 192.168.1.10 -port 5041
```

Through the internet, no port forwarding needed:

```powershell
dptdoom2d-master.exe --port 25145 --relay-port 25146
dptdoom2d.exe -net host -master 1.2.3.4:25145 -relay
dptdoom2d.exe -net join -master 1.2.3.4:25145 -relay-session <id>
```

Lockstep: both machines run the same world from the same RNG seed; only input
crosses the wire, plus a world checksum so a desync is reported instead of
silently drifting.

## Documentation

Russian, in `docs/`:

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — how the port works
- [docs/FORMATS.md](docs/FORMATS.md) — data formats, including XM
- [docs/NETWORK.md](docs/NETWORK.md) — protocol, master, relay
- [docs/BUILDING.md](docs/BUILDING.md) — build, CI, checks
- [docs/CONTROLS.md](docs/CONTROLS.md) — keys, the F2 overlay, cheats
- [docs/ROADMAP.md](docs/ROADMAP.md) — remaining work
- [docs/PORTING.md](docs/PORTING.md) — port log

## License

GPLv2 — see [LICENSE](LICENSE). The port is a derivative of Doom2D:Rembo
(GPLv2). Game data is fetched from upstream and is not part of this repository.
