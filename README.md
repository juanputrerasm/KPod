# KPod

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)](#requirements)
[![.NET](https://img.shields.io/badge/also%20builds%20for-.NET%2010-512BD4)](#building-from-source)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6)](#requirements)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

**Terminal Reality POD archive utility for Windows.**

KPod opens, browses, previews, extracts and builds the `POD` archives used by
*Monster Truck Madness 1 & 2*, *CART Precision Racing*, *Hellbender*, *Terminal
Velocity*, *Fury3*, *Nocturne*, *4x4 EVO 1 & 2* and their relatives. It reads and
writes the `POD1` format and reads and explicitly authors `POD2`; `EPD` remains
read-only.

It started as a Windows-native port of
[JPod](https://github.com/juanputrerasm/JPod), the Java 17 original, and matches
it byte for byte: the same archives, the same `.inf` and `.lst` reports, the same
format names.

![KPod browsing ALASKA.POD with an ACT palette previewed](docs/screenshot.jpg)

*Browsing `CRAZY98.POD` while previwing a BIN model*

---

## Supported formats

| Format | Games | Support |
|---|---|---|
| `POD1` | MTM 1 & 2, CPR, Hellbender, Terminal Velocity, Fury3 | browse, preview, extract, save |
| `POD2` | Nocturne, 4x4 Evo 1 & 2 | browse, preview, extract, explicit save/conversion, CRC, timestamp and audit history |
| `EPD` | Fly! | browse, preview, extract |

---

## Features

### Archive viewing
- Open `.pod` and `.epd` archives and browse them in a file-viewer style list with
  Name / Size / Description columns.
- Folders and subfolders are shown with icons and start collapsed, so root files
  and top-level folders are easy to scan.
- Click a column header to sort by name, size or description; click again to
  reverse it, and a third time to return to archive order.
- The filter box narrows the list as you type, and it overrides collapse so a
  match is never hidden. The Advanced button opens a search dialog that jumps the
  list to a result.
- The 80-character archive comment is editable below the toolbar.
- The 10 most recently opened files are remembered in
  `%APPDATA%\KPod\config.json`.
- Preferences are read the first time something needs them rather than at launch, so
  starting up costs no disk read at all.

### Extraction
- **Extract All** writes every entry to a folder, recreating the archive's
  subfolder structure.
- **Extract Sel.** extracts the selection, with an option to preserve or flatten
  the folder structure. Selecting a folder heading selects everything under it.

### Building and editing
- **New Archive** starts an empty archive.
- **Open Response List File** loads the existing `.lst` syntax or a PODTool-style
  `.rsp` with `podFilename:`, `volumeName:`, `//` comments and relative paths.
- **Add Files / Add Folder** appends files or recursively imports a directory.
- **Folder editing** creates folders, renames file or folder trees, and moves
  selections by command or internal drag and drop while preserving case and order.
- **Remove** deletes the selection.
- **Replace with File** swaps one entry's data, keeping its archive name.
- **Disable / Enable in Game** hides a track or truck from the game the way
  CommPatch 26 does, by renaming it to an extension the engine does not recognise:
  `.trk` becomes `.trx`, `.sit` becomes `.six`, and `.si2` becomes `.siy`. Nothing
  is deleted and nothing is re-encoded, so enabling puts the addon back exactly as
  it was. The item only appears for a selection that has something to rename.
  Changing a track pod's status can trigger the multiplayer *Different Version*
  message, which the status bar says after each rename.
- **Validate Archive** checks limits, field capacity, unsafe and duplicate paths,
  payload ranges, RAW palette records, checksums and the expected output layout.
- **Save / Save As** saves a named archive or writes a copy. POD1 is the default;
  POD2 is an explicit target and the actual output format is shown before writing.

### Reports
- **Save .inf** exports the fixed-column report: file name, total size, entry
  count, comment, and a padded name / size / offset table.
- **Save .lst** exports a plain entry-name list, ready to feed back into the
  response-list loader.

### File preview
Double-click any entry, or use Preview from the right-click menu:

| Extension | Preview |
|---|---|
| `.raw`, `.clr` | 8-bit paletted image with a live palette selector and Save as BMP. Art textures (64x64) are drawn at 4x with no smoothing; unknown sizes open a dimensions dialog. |
| `.act` | 16x16 colour swatch grid; hovering a swatch reports its index and hex value. |
| `.wav` | Audio player with play, pause/resume, stop, seek bar and formatted time display. |
| `.mod` | Standard tracker-module playback with the same transport and seeking, once the optional [MOD playback add-on](#mod-playback-add-on) is installed. |
| `.png` | Alpha-preserving PNG preview over a transparency checkerboard. |
| `.tga` | Uncompressed or RLE 24/32-bit true-colour Targa preview, including alpha and image-origin flags. |
| `.bin` | Native OpenGL 3.3 model viewer with textures, normal maps, material effects, lighting/wireframe/grid controls, diagnostics and clickable texture thumbnails. An animated BIN opens on frame 1 with a banner naming the frame and where it resolved from; **Play** or the **A** key steps through the frames. |
| `.smf` | 4x4 Evolution 1 and 2 models, in the same viewer. |
| `.tif` | 4x4 Evolution 2 palette-indexed TIFF, including the second sample it uses as an opacity plane. |
| `.bmp`, `.jpg`, ... | Standard images through GDI+. |
| `.txt`, `.def`, `.nav`, `.lvl`, `.sit`, `.si2`, `.six`, `.siy`, `.trk`, `.trx`, `.txv`, `.lst`, `.ini`, `.cfg`, `.tex`, `.tnl`, `.ttx`, `.trn`, `.ndx`, `.veg`, `.wat` and other text formats | Scrollable monospaced text. |
| anything else | Hex dump of the first 4 096 bytes. |

#### 4x4 Evolution models

`.smf` is 4x4 Evolution's static model format, a text `C3DModel` covering versions 2
to 4 including Evo 2's `v1` bump materials. It opens in the same viewer as `.bin`,
and is recognised by its magic rather than its extension, so an entry whose name is
missing or wrong still opens as the model it is.

Evo is Y-up where `.bin` is Z-up, and Evo's texture V runs top-down where `.bin`'s
does not, so the decoder converts both into the `.bin` convention as it reads. The
renderer then treats every model alike, with no per-format branching. That conversion
negates Evo's Z on the way to the screen; leaving it out renders the model as its own
mirror image, which reads as the texture being mirrored because a mirrored mesh still
carries its own UVs. Its groups are drawn double-sided, because Evo's
foliage, fences and banners are single-sided sheets meant to be seen from behind.

Diffuse textures resolve `.png`, `.tga`, `.tif`, then `.raw`. An Evo `.raw` also
picks up its same-stem `.opa` opacity plane, which is a real 0-255 gradient rather
than a mask, so it is merged into the alpha channel instead of being reduced to the
colour key the MTM family needs; art that carries its own alpha is alpha-tested on
that channel and never colour-keyed. Reduced-detail LOD groups are hidden when a
model carries its full-detail pair, and kept when they are all it has.

#### Palette resolution
For `.raw` and `.clr` entries the palette is resolved in this order:

1. the palette recorded in the entry's own POD directory field (see below)
2. the same base name with an `.act` extension, in the same archive directory
3. `METALCR2.ACT` anywhere in the archive
4. `VGA.ACT` anywhere in the archive
5. the bundled MTM1 `metalcr2.act`

The archive's own `METALCR2.ACT` outranks the bundled copy because METALCR2 is not
one palette: CPR ships a different one from MTM1 and MTM2, so the copy the pod
carries is the one known to match its art. A `VGA.ACT` in the pod says the archive
belongs to one of the flight games, and since nothing at this point tells Terminal
Velocity from Fury3 or Hellbender, that copy is the only one that can be trusted.

An `.act` whose name matches nothing is never guessed at. The preview selector
offers those last, along with the bundled CPR, Hellbender and Terminal
Velocity/Fury3 palettes and greyscale, and none of them is ever chosen
automatically. The last manual choice is remembered when another RAW offers the
same option.

The BIN viewer follows the same rules. Its selector supplies the fallback for
model textures that have no same-name `.act` of their own, so it is ranked from
the point of view of a texture that has none: another texture's palette is a
guess, and METALCR2 is not.

Step 1 is the one that matters for MTM1, Terminal Velocity, Fury3 and Hellbender.
Their packer stored the palette each RAW was authored against in the spare bytes
of the entry's name field, and it is not derivable from the file name: on those
games' main archives a name-based guess picks the wrong palette for 8 221 of 8 224
RAW entries. MTM2, CPR and community archives carry no such record and fall
straight through to the name-based rules. [`docs/POD_FORMAT.md`](docs/POD_FORMAT.md)
specifies it in full.

When a RAW payload is not one of the recognized sizes, the preview dialog offers
the exact width and height pairs that match the byte count, a swap button, and
every palette in the archive. The three common screen sizes are recognized and
opened directly:

- `64000` bytes: `320 x 200`
- `256000` bytes: `640 x 400`
- `307200` bytes: `640 x 480`

#### BIN model viewer

BIN previews use a native OpenGL 3.3 context. Drag with the left mouse button to
orbit, use the mouse wheel to zoom, the left/right arrow keys to strafe, and
**Reset view** to refit the model. The toolbar controls textures, wireframe, grid,
smoothing, lighting direction and background colour. Classic transparency and
Extended BIN alpha, blend, additive, two-sided, depth-write, tint, emissive,
specular, normal-strength and TEXSOLID material states are applied by the GPU.

Textures are found in `ART`, `MODELS`, `DATA`, `TEXTURES`, then the archive root
and title fallback, with PNG preferred over TGA, TIF and RAW. `_N.PNG` and `_N.TGA`
normal maps use the games' DirectX/green-down convention. A same-name ACT remains
authoritative for a RAW texture; unresolved RAW textures share the selectable
metadata/archive/bundled fallback palette. The bottom strip reports missing or
non-standard assets, and clicking a resolved thumbnail opens it in the normal
image preview. The surface picks its pixel format by enumerating what the device
actually publishes rather than by handing `ChoosePixelFormat` an ideal descriptor,
which on a software renderer can score a plain GDI format as the closest match and
fail several calls later with no explanation. Systems without an OpenGL 3.3-capable
display driver show a recoverable explanation instead of closing KPod, naming the
renderer and OpenGL version the driver reports.

On a machine whose display driver publishes no usable OpenGL (ARM VMs, etc), putting a software
implementation's `opengl32.dll`, Mesa's llvmpipe build for example, next to `KPod.exe`
replaces the system renderer for this process. KPod loads it by explicit full path
before its first OpenGL call, so any companion the renderer needs resolves from that
same folder. There is nothing to install, no registry entry, and no effect on any other
program. Nothing ships with KPod and nothing is required: with no such file present the
system renderer is used, and the BIN preview's diagnostics line names whichever one is
in play. It costs frame rate in exchange for working at all, which is why it is opt-in.

Copy the whole renderer package, not just `opengl32.dll`. Mesa's WGL build is a loader
that needs its companions, `libgallium_wgl.dll` among them, in the same folder, and it
has to be the 32-bit build because KPod is a 32-bit program. A file that is present but
cannot be loaded is reported with the reason rather than ignored.

Pixel formats are enumerated through the loaded `opengl32.dll`'s own `wgl` exports when
a drop-in renderer is in use, and through GDI otherwise. GDI's `ChoosePixelFormat`
family answers for the system display driver, so it reports nothing usable about a
replacement renderer's formats; presentation goes back through whichever of the two the
format came from.

### Mount in pod.ini
Adds the open archive to the game's `pod.ini` mount list. KPod treats 99 as the
recommended limit, warns when the list is already that long, and still mounts the
POD rather than blocking. It searches the archive's folder and its parent, and
refuses to mount the same POD twice.

### Startup
The .NET Framework build turns on **multicore JIT** at launch. The first run records
which methods starting up actually compiles, into
`%LOCALAPPDATA%\KPod\startup.profile`; later runs replay that list on a background
thread so the JIT works ahead of the code rather than being asked for one method at a
time. It is best effort, so a machine that will not let the profile be written simply
starts the way it did before. Deleting the file costs one slower start and nothing
else.

This is the Framework build's substitute for the ReadyToRun precompilation the .NET
build gets at publish time. NGen would do more, but it needs an installer, and KPod is
meant to be copied rather than installed.

### Performance
Opening an archive reads its directory, not its contents. A POD directory is a few
kilobytes even when the archive is hundreds of megabytes, so entry payloads stay on
disk until a preview, an extract or a save actually asks for one. The practical
effect is that archive size barely affects how long an archive takes to open, or how
much memory holding it open costs.

Measured on the .NET 10 build, opening these archives and then browsing, sorting and
filtering all 4341 entries of Hellbender's `GAME.POD`:

| Archive | Size | Entries | Open | Held after open |
|---|---:|---:|---:|---:|
| `UI.pod` (Community Patch 2) | 192 MB | 462 | 2.2 ms | 130 KB |
| `GAME.POD` (MTM1) | 51 MB | 2789 | 1.0 ms | 709 KB |
| `GAME.POD` (Hellbender) | 41 MB | 4341 | 1.3 ms | 1.1 MB |
| `FURYSE.POD` (Fury3) | 31 MB | 1330 | 0.5 ms | 346 KB |

On Hellbender's `GAME.POD`, expanding every folder takes 2.5 ms, sorting the whole
list by description 3.0 ms, four filter keystrokes 5.2 ms, and selecting a 3420-entry
folder and mapping the selection back to rows 2.6 ms.

Saving streams too. The writer plans the directory from names and lengths, then
copies each payload through a single 64 KB buffer into a temporary file beside the
target and swaps it in, so peak memory for a save is the directory plus that buffer
regardless of archive size. Rewriting the 192 MB `UI.pod` in place takes 120 ms, and
because the new archive is complete on disk before it replaces the old one, a failed
save cannot damage the original.

---

## The POD1 name budget

MTM2 reads exactly one POD layout. Its directory entries are 40 bytes:
`char name[32]`, `int32 size`, `int32 offset`, holding paths of up to 31
characters. A directory table that does not validate as 40-byte records is a
malformed archive, and KPod refuses it.

KPod refuses a name that does not fit rather than truncating it. A truncated name
packs without error and then simply never resolves in game, which is far harder to
diagnose than a refusal. The complete record counts against the 31 bytes: the path,
its terminator, and on a `.RAW` entry any embedded palette name and its terminator.

Authoring guidance goes further and keeps the *stem* to 18 characters, because the
engine builds sibling names from it and every derived name must still fit 31.
`ART\` plus an 18-character stem plus `_MASK` plus `.PNG` is exactly 31.

---

## Requirements

Windows, and nothing else. KPod runs on .NET Framework 4.8, which is part of
Windows 10 (from the April 2018 update) and Windows 11, so there is nothing to
install. It also runs on Windows 7 SP1 and 8.1 if 4.8 is present.

A second build targeting .NET 10 is published for anyone who prefers it. That one
needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
The two are identical in behaviour; pick whichever suits the machine.

---

## Getting started

Prebuilt binaries are on the
[Releases](https://github.com/juanputrerasm/KPod/releases) page. Download
`KPod.exe` and run it. No installer, no dependencies, one file. The .NET
Framework 4.8 build is the one to take unless you specifically want the .NET 10
build, which is published alongside it.

1. **Open...**, or drag a `.pod` onto the exe, or pass a path on the command line.
2. Double-click a folder row to expand it, or use **Expand +** to open everything.
3. Double-click a file to preview it.
4. Select entries and press **Extract Sel.**, or **Extract All** for the lot.

> [!NOTE]
> The executable is unsigned, so Windows SmartScreen will warn on first run.
> Choose *More info* then *Run anyway*. If you copied the file from another
> machine you may also need `Unblock-File .\KPod.exe` in PowerShell.

---

## Usage

### Toolbar

Actions worth naming carry a label, the rest are icon-only with a tooltip, and the
ones that come in families sit behind a split button whose face runs the most common
of them. Everything here is also in the menus, so no action is reachable only through
a dropdown.

| Control | Face runs | Arrow opens |
|---|---|---|
| **New** | New archive | New Archive, Open Response List File |
| **Open** | Open a POD or EPD archive | Open POD, Open Response List File, recent files |
| **Save** | Save over the open archive | |
| **Save As** | Write the entry list to a new archive | |
| **Add** | Add Files | Add Files, Add Folder, Create Folder |
| **Extract** | Extract Selected | Extract Selected, Extract All |
| Remove | Drop the selected entries from the list | |
| Expand All / Collapse All | Open or close every folder | |
| Search | Open the search dialog | |
| About | Version and credits, at the right edge | |

The icons are glyphs from **Segoe MDL2 Assets**, the icon font Windows draws its own
command bars with, rendered at the monitor's DPI so they stay sharp when the window
moves between displays. That font arrived with Windows 10; on Windows 7 SP1 and 8.1
the buttons fall back to plain text labels.

### Menus

- **File**: Open POD, New Archive, Open Response List File, Open Recent, Add
  Files, Remove Selected, Save As, Extract All, Extract Selected, Save .inf
  Report, Save .lst List, Exit
- **Tools**: Mount in pod.ini, Search
- **Help**: About

### Status bar

`Size` is what the archive would occupy if saved now, which is why it changes as
entries are added and removed. `Files` and `Selected` are counts, `unsaved`
appears once the list differs from what is on disk, and the square at the right
edge turns red while an operation is running.

---

## Building from source

Building needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
which compiles both targets. Running the result needs only what the table above
says.

```sh
dotnet restore KPod.slnx
dotnet build   KPod.slnx          # builds net48 and net10.0 together
dotnet test    KPod.slnx -f net10.0
```

All three work on macOS and Linux as well as Windows. `KPod.Windows` sets
`EnableWindowsTargeting`, so the WinForms project compiles anywhere even though it
only runs on Windows. The test project also targets `net48`, but running that leg
needs a Windows machine or Mono; `-f net10.0` is the portable way to run the suite.
To run the app on Windows:

```sh
dotnet run --project KPod.Windows -f net48
```

Release builds:

```sh
# .NET Framework 4.8, the main download
dotnet publish KPod.Windows -c Release -f net48 -p:DebugType=none

# .NET 10, the alternative
dotnet publish KPod.Windows -c Release -f net10.0-windows -r win-x86 \
  --self-contained false -p:PublishSingleFile=true -p:DebugType=none
```

Both produce a single executable. The Framework build compiles `KPod.Core`
straight into the exe and uses Costura.Fody 6.2.0 to embed the managed Silk.NET
dependencies, since .NET Framework has no single-file publisher.

Nothing about MOD playback is inside the executable. See
[MOD playback add-on](#mod-playback-add-on) below.

The .NET 10 publish turns on **ReadyToRun**, which compiles the app's IL to native
code ahead of time so starting it does not have to JIT its own code first. It applies
automatically whenever a runtime identifier is given, which the command above does,
and it costs about 1.7 MB of executable size. Pass `-p:PublishReadyToRun=false` to
trade the startup back for the smaller file.

Do **not** add `-p:PublishReadyToRun=true` to the net48 command. ReadyToRun is a .NET
Core feature that does nothing for .NET Framework, and asking for it makes the SDK
pick a runtime identifier from whatever machine is doing the build, so publishing
from a Mac quietly produces an ARM64 binary in `bin/Release/net48/osx-arm64/` that no
Windows PC will run. The project fails the build with an explanation if you try. The
.NET Framework build has no ReadyToRun equivalent short of NGen, which needs an
installer rather than an xcopy.

### MOD playback add-on

`.wav` playback is built in. Tracker modules need libopenmpt, which is not part of
KPod.exe: it is a separate download, the official libopenmpt 0.8.9 release for 32-bit
Windows. The repository ships it in [`mod-playback/`](mod-playback), which is the
folder to zip.

To install it, copy five DLLs into the folder that holds `KPod.exe`:

```
libopenmpt.dll
openmpt-mpg123.dll
openmpt-ogg.dll
openmpt-vorbis.dll
openmpt-zlib.dll
```

That is the entire installation. Nothing is written to the registry, nothing is
copied under `%LOCALAPPDATA%`, and no network connection is used at any point.
Deleting the five files uninstalls it.

Opening a `.mod` entry is what triggers the load. KPod checks that all five files are
beside the executable, then loads `libopenmpt.dll` by explicit full path with
`LOAD_WITH_ALTERED_SEARCH_PATH`, which is what lets the four companions resolve out of
that same folder and nowhere else. If a file is absent, or Windows refuses to load
one, the audio player opens with its transport disabled and says **MOD playback is
not installed**, naming the first missing DLL. Everything else in KPod, WAV playback
included, is unaffected. Only a successful load is cached, so dropping the DLLs in and
reopening the entry works without restarting KPod.

The add-on carries its own licenses. When they are present beside the executable,
**About → Third-party notices** appends them to the notices built into the exe, so
the dialog describes what is actually installed.

Keeping libopenmpt out of the executable is what took the net48 download from about
9 MB to about 1.8 MB.

### Architectures

KPod is **x86**, on both target frameworks. One 32-bit binary runs on 32-bit Windows,
on x64 under WOW64, and on ARM64 under emulation, so a single download covers every
Windows PC. Being 32-bit is also what lets the MOD playback add-on be a single set of
DLLs rather than three.

`PlatformTarget` is set in `KPod.Windows.csproj`, so the net48 command above needs no
extra flags. The .NET 10 build additionally needs the matching runtime identifier,
because that is what lets it be a single file and be precompiled; `-r win-x86` is the
only supported value and any other one fails the build.

### Writing for both targets

Almost all of the code is shared, with one `#if` for WinForms startup and one for
a folder-dialog property. Where .NET Framework lacks something, `KPod.Core/Compat`
supplies it rather than the calling code branching: `Index`, `Range`, records
support, path helpers, Latin-1 lookup, JSON, and Java-compatible trimming. The
Windows project uses Silk.NET's OpenGL bindings for BIN previews; Costura embeds
those dependencies into net48 and the .NET single-file publisher handles the
modern builds.

### Project layout

| Project | Target | Purpose |
|---|---|---|
| `KPod.Core` | `net48`, `net10.0` | Archive reading and writing, image and palette decoding, reports, manifests, `pod.ini`, preferences, and the folder-browser model. No Windows API, so it runs anywhere |
| `KPod.Windows` | `net48`, `net10.0-windows` | WinForms UI, plus the Windows-only pieces under `Platform/` (MCI audio) |
| `KPod.Tests` | `net48`, `net10.0` | Portable xUnit tests for `KPod.Core` |
| `KPod.Windows.Tests` | `net48`, `net10.0-windows` | Windows-only decoding, PCM, seeking, lifecycle and audio-UI tests, plus the add-on probe. Builds copy `mod-playback/` into the test output so the decoder tests have a libopenmpt to load |

`KPod.Core` must never reference `System.Drawing` or `System.Windows.Forms`. The
RAW decoder returns `int[]` pixels and only `KPod.Windows` turns them into a
`Bitmap`; that one rule is what keeps the test suite portable.

### Regenerating the icon

`KPod.Windows/Assets/build-icon.sh` rebuilds `KPod.ico` from the two SVGs using
stock macOS tools. The full artwork is used at 48px and above; the small entries
use the K-only variant, because the slabs turn to mush below 32px.

---

## Format specification

[`docs/POD_FORMAT.md`](docs/POD_FORMAT.md) specifies the whole POD family: POD1,
POD2 and EPD. It covers the palette record
the early Terminal Reality packer stored in the spare bytes of each RAW entry's
name field, which is documented nowhere else, and it marks every claim as verified
against shipped archives, documented upstream, implemented but unconfirmed, or
unresolved. [`docs/pod-format.json`](docs/pod-format.json) carries the same
structures as machine-readable data.

## References

- [JPod](https://github.com/juanputrerasm/JPod) - the Java 17 original
- [KPodman](https://github.com/juanputrerasm/KPodman) - the POD mounting utility
  this shares its build with
- [MTM2 Engine Content Limits](https://www.mtm2.com/~mtmg/misc/ENGINE_LIMITS.md)
- [EPD Format Reference](https://github.com/jopadan/termpod/wiki/EPD-Format-Reference)
- [Pod 2 Format Reference](https://github.com/jopadan/termpod/wiki/Pod-2-Format-Reference)

---

## Credits

Developed by **Juan Pablo Utreras** for the Monster Truck Madness Guild.

Based on the original WinPod by MDMRE.

Licensed under the [Apache License 2.0](LICENSE).

Monster Truck Madness, Terminal Velocity, Fury3, Hellbender, CPR, Nocturne and
4x4 Evo are trademarks of their respective owners. This is an unofficial,
community-made utility with no affiliation to Microsoft or Terminal Reality.
