# Doom 2D — native Win32 port

**Doom 2D** (Prikol Software, 1996) rewritten in C# on a native Win32 layer.
Same gameplay, no third-party libraries: the window is `user32`, the frame is
`gdi32`, the sound is `winmm`.

- **[README.md](../README.md)** — overview, screenshots, quick start (Russian)
- **[ARCHITECTURE.md](ARCHITECTURE.md)** — layers, the Win32 platform, module map
- **[FORMATS.md](FORMATS.md)** — WAD, sprites, maps, saves, DMI, XM
- **[NETWORK.md](NETWORK.md)** — lockstep protocol, master server, relay
- **[BUILDING.md](BUILDING.md)** — build, packaging, CI, headless checks
- **[CONTROLS.md](CONTROLS.md)** — keys, the F2 overlay, cheats
- **[ROADMAP.md](ROADMAP.md)** — what is left
- **[PORTING.md](PORTING.md)** — port log: decisions and pitfalls

Everything except this file is in Russian; the code and its comments are in
English, so a developer who does not read Russian can still work on it.

## Layout

| Project | Output |
|---|---|
| `DPTDOOM2D` | `dptdoom2d.exe` — the game |
| `DPTDOOM2D.Launcher` | `dptdoom2d-launcher.exe` — launcher |
| `DPTDOOM2D.Master` | `dptdoom2d-master.exe` — master server + relay |
| `DPTDOOM2D.Shared` | master protocol (compiled into the game as well) |

License: GPLv2 (see [../LICENSE](../LICENSE)).
