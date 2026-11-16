# FNA native libraries (win64)

FNA is a managed reimplementation of XNA, but the platform layer underneath it
is native. These four DLLs have to sit next to `Doom2D.exe`:

| File | What it is | License |
|---|---|---|
| `SDL2.dll` | window, input, threads, filesystem | zlib |
| `FNA3D.dll` | graphics (D3D11 / OpenGL / Vulkan backend) | Ms-PL |
| `FAudio.dll` | audio mixer, replaces XAudio2 | zlib |
| `libtheorafile.dll` | Ogg Theora video decoding | zlib-style (see upstream) |

They are committed because a CI artifact that cannot start is not an artifact.
All four licenses allow redistribution of binaries; they are unmodified builds
taken from the FNA ecosystem, and `LICENSE` in the repository root lists them as
separate components with their own terms.

They came from the TerrariaXDK donor tree, which carried a known-good FNA
setup. To use a different (newer) set, either replace the files here or point
`TXDK_NATIVE_DIR` at another folder - `tools/build.ps1` prefers that variable.

Upstream:
- https://github.com/FNA-XNA/FNA
- https://github.com/FNA-XNA/FNA3D
- https://github.com/FNA-XNA/FAudio
- https://www.libsdl.org/
