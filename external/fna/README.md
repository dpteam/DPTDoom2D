# FNA (vendored)

This folder is a copy of the FNA sources, the XNA 4.0 reimplementation that
Doom 2D runs on. Nothing here is modified: the project links these files
(`<Compile Include="..\external\fna\..." />`) rather than editing them.

**Why vendored.** The files were originally taken from the TerrariaXDK donor
tree, which is a ~1.2 GB decompiled game. Copying only the FNA part (1.3 MB)
lets the repository build on its own, without the donor and without network
access.

**Origin.** https://github.com/FNA-XNA/FNA — the donor tree carried a trimmed
copy: the `Microsoft.Xna.Framework*` namespaces, `SDL2/`, `MonoGame.Utilities/`,
`ObjCRuntime/`, plus `FAudio.cs`, `Theorafile.cs`, `CRC32Helper.cs`,
`ToStringExtensions.cs`, `StorageDeviceManager.cs` and the compiled stock-effect
blobs (`Microsoft.Xna.Framework.Graphics.Effect.Resources.*.fxb`).

**One local change.** The donor's `SpriteBatch.cs` calls
`Terraria.Program.LogWarn()` when a Begin/End pair does not balance. Rather than
patching a vendored file, the port supplies a shim:
`Doom2D/Fna/TerrariaLogShim.cs`.

**Native libraries are not here.** FNA needs `SDL2.dll`, `FNA3D.dll`,
`FAudio.dll` and `libtheorafile.dll` at run time. They are binaries, so they are
not committed; `tools/build.ps1` copies them from a donor tree or from
`TXDK_NATIVE_DIR` (see `Doom2D/README.md`).

**License.** Microsoft Public License (Ms-PL), see `LICENSE` in this folder.
The Ms-PL requires that this notice stays with the sources.
