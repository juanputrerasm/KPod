# KPod migration plan

KPod is the Windows-native port of [JPod](../JPod), the Java 17 Terminal Reality
POD archive utility. It stands in the same relation to JPod that
[KPodman](../KPodman) stands to JPodman: same behaviour, same file formats, same
on-disk artefacts, rewritten in C# on .NET Framework 4.8 so it runs on a stock
Windows install with nothing to download.

This document is the plan for that port. It fixes the target specification,
maps every source file, orders the work into phases with exit criteria, and
records the decisions that cannot be deferred.

---

## Status

Phases 0 through 7 are implemented. `dotnet build KPod.slnx` is clean on both
target frameworks, `dotnet test KPod.slnx -f net10.0` passes 112 tests, and both
publish commands produce a single executable.

Verified against a real archive rather than only fixtures. Reading `FURYSE.POD`
(1 330 entries, 32 141 732 bytes) and writing it back out produces bytes identical
to what JPod produces from the same archive, and the `.inf` and `.lst` reports are
identical to JPod's byte for byte. That cross-check found one real defect: the C#
reports were being written with a UTF-8 byte-order mark that Java does not emit.
Fixed, and covered by a test.

### The early packer records each RAW's palette in its directory field

The 32-byte POD1 name field holds a NUL-terminated path. The early Terminal
Reality packer also wrote a **second** NUL-terminated string after it, on every
`.RAW` entry: the name of the `.ACT` palette that art was authored against.

Verified across every archive available locally:

| Game | Archives with it | RAW entries carrying a palette |
|---|---|---:|
| MTM1 | `GAME.POD`, `STARTUP.POD`, `TRUCK.POD` | 2 450 |
| Terminal Velocity | `CDROM.POD` | 877 |
| Fury3 | `FURY3.POD`, `FURYSE.POD`, `STARTUP.POD` | 2 752 |
| Hellbender | `GAME.POD`, `STARTUP.POD` | 3 888 |
| MTM2 | none of 19 | 0 |
| CPR | none of 22 | 0 |

The rules hold without exception across all of it: only `.RAW` entries carry one,
there is never a third string, and the name always ends in `.ACT`. It is a clean
generational split, present in the earlier engine family and dropped by MTM2 and
CPR. Nothing upstream documents it; `ENGINE_LIMITS.md` describes a single
31-character path in that field.

Two consequences, both now handled.

**Previews were using the wrong palette.** The resolver's second rule, "any `.ACT`
in the same directory", takes whichever palette happens to come first in the
archive. Measured against the stored name on MTM1 `GAME.POD`, Fury3 `FURY3.POD`,
Terminal Velocity `CDROM.POD` and Hellbender `GAME.POD`, it disagreed on 8 221 of
8 224 RAW entries. The stored palette is now consulted first, ahead of the
same-name rule, and falls through to the old chain when there is none. 10 709 of
the 10 844 stored palettes now resolve to exactly the named `.ACT`; the remaining
135 are MTM1 `TRUCK.POD` entries naming `METALCR2.ACT`, which lives in `GAME.POD`
rather than in `TRUCK.POD`, so the fallback supplies the bundled copy of that same
palette.

**Re-saving destroyed it.** The writer rebuilt each field from the name, so the
second string was dropped. An entry read from an archive now keeps its directory
field and the writer puts those bytes back verbatim when they fit the layout being
written; entries added from disk or a manifest get a field built from the name as
before. All 21 archives above re-save byte for byte identical, in JPod and KPod
alike.

One limit moved as a result: the writer capped archives at 4 096 entries while the
reader accepted 8 192, so Hellbender's `GAME.POD` (4 341 entries) could be opened
but not saved. The writer now matches the reader.

What is not done, because it needs a Windows machine:

- `MANUAL-CHECKLIST.md` has not been walked through. Everything in it is
  unverified behaviour, the GUI included.
- The `net48` test leg compiles but has not been run; running it needs Windows or
  Mono. `-f net10.0` is the portable way to run the suite.
- `docs/main.jpg`, the README screenshot, is missing and needs a real window.

Two design choices differ from what this plan assumed, both for the better:

- The folder-tree browser is `KPod.Core/Session/EntryBrowser.cs`, not logic inside
  the form. It is plain logic over names and sizes, so it is unit tested rather
  than left to the manual checklist.
- The entry describer and palette resolver moved into `KPod.Core` for the same
  reason; JPod keeps both inside its UI classes.

---

## 1. Goals

- **One file, no dependencies.** `KPod.exe` runs on Windows 10 (April 2018
  update or later) and Windows 11 as shipped, because .NET Framework 4.8 is part
  of the OS. No installer, no runtime download, no NuGet package at runtime.
- **Behaviour parity with JPod 1.3.0.** Every feature in the JPod README works
  the same way, produces byte-identical `.pod`, `.inf` and `.lst` output, and
  reports the same format names.
- **Full POD1-64 support from day one.** KPod reads and writes the Community
  Patch 3 extended directory, exactly as JPod now does.
- **A second .NET 10 build**, published alongside, identical in behaviour, for
  anyone who prefers it.
- **Testable without Windows.** The domain and file-processing layer builds and
  tests on macOS and Linux so development does not require a Windows machine.

### Non-goals

- No POD3+ support, no in-place archive editing, no MOD playback. JPod does not
  do these and neither does KPod.
- No feature additions during the port. New behaviour lands after parity, not
  during it.
- No shared code with KPodman at build time. The two repositories stay
  independent; shared files are copied and diverge freely.

---

## 2. Target specification

The same specification KPodman uses, changed only where the product name
differs.

| Item | Value |
|---|---|
| Primary target | `net48` (.NET Framework 4.8) |
| Secondary target | `net10.0` / `net10.0-windows` |
| SDK | .NET 10 SDK, pinned in `global.json` (`10.0.302`, `rollForward: latestFeature`) |
| UI framework | WinForms |
| Language | C# `latest`, nullable enabled (`annotations` only on the `net48` leg) |
| Runtime NuGet packages | none |
| Build-time packages | `Microsoft.NETFramework.ReferenceAssemblies` (net48 leg), xUnit stack (tests only) |
| Architecture | AnyCPU for net48, `win-x64` for the .NET 10 publish |
| Publish result | a single `KPod.exe` per target |

### Why `Nullable` drops to `annotations` on net48

.NET Framework reference assemblies carry no nullable annotations, so full
checking reports a false positive on virtually every BCL call. The `net48` leg
keeps the annotations and drops the warnings; the .NET 10 leg does the real
checking. This is already encoded in KPodman's `Directory.Build.props` and is
copied verbatim.

### Why the net48 leg compiles Core into the exe

.NET Framework has no single-file publish and no assembly merge tool in the SDK.
`KPod.Windows` therefore compiles `KPod.Core`'s sources directly on the `net48`
leg with a `<Compile Include="..\KPod.Core\**\*.cs" Link="Core\..." />` item,
excluding `bin` and `obj`, and takes a normal `ProjectReference` on the .NET 10
leg. One executable, no merge step.

---

## 3. Repository layout

```
KPod/
├── Directory.Build.props        Shared version, product metadata, nullable policy
├── global.json                  SDK pin
├── KPod.slnx                    Solution
├── LICENSE                      Apache 2.0 (already present)
├── README.md                    Rewritten from the JPod README, KPodman structure
├── MIGRATION_PLAN.md            This document
├── docs/
│   └── main.jpg                 Main-window screenshot for the README
├── KPod.Core/                   net48; net10.0
│   ├── Compat/                  net48 shims, copied from KPodman
│   ├── Pods/                    POD1, POD1-64, POD2, EPD reading and writing
│   ├── Images/                  RAW / CLR / ACT decoding to pixel arrays
│   ├── Reports/                 .inf and .lst export
│   ├── Manifests/               .lst response-file parsing
│   ├── PodIni/                  pod.ini mounting
│   ├── Preferences/             config.json load and save
│   └── Session/                 PodSession, EditableEntry
├── KPod.Windows/                net48; net10.0-windows
│   ├── Program.cs               Entry point, fatal-error reporting, startup context
│   ├── app.manifest             DPI awareness for the net48 leg
│   ├── UI/                      WinForms forms and dialogs
│   ├── Platform/                Windows-only pieces (audio, shell, drag and drop)
│   ├── Assets/                  KPod.ico, source SVGs, build-icon.sh
│   └── MANUAL-CHECKLIST.md      GUI checks automated tests cannot reach
└── KPod.Tests/                  net48; net10.0 - xUnit
```

`KPod.Core` must not reference `System.Drawing`, `System.Windows.Forms`, or any
Windows-only API. That constraint is what keeps the test project runnable on any
OS, and it is the single most important structural rule in this port: the RAW
decoder returns `int[]` pixels, and only `KPod.Windows` turns them into a
`Bitmap`.

---

## 4. Source mapping

JPod is 20 source files, about 4 455 lines. Every one has a destination.

| JPod (Java) | Lines | KPod (C#) | Notes |
|---|---:|---|---|
| `JPodApp.java` | 29 | `KPod.Windows/Program.cs` | Plus `StartupContext`, unhandled-exception reporting |
| `PodSession.java` | 96 | `KPod.Core/Session/PodSession.cs` | Plain properties |
| `AppConfig.java` | 207 | `KPod.Core/Preferences/AppConfig.cs` + `ConfigStore.cs` | Hand-rolled JSON parser replaced by `Compat/Json.cs` |
| `io/pod/PodArchive.java` | 178 | `KPod.Core/Pods/PodArchive.cs`, `PodEntry.cs`, `PodFormat.cs` | `Entry` record becomes a C# record; needs `IsExternalInit` on net48 |
| `io/pod/PodArchiveReader.java` | 295 | `KPod.Core/Pods/PodArchiveReader.cs` | Start from KPodman's file, add POD1-64 detection |
| `io/pod/PodArchiveWriter.java` | 187 | `KPod.Core/Pods/PodArchiveWriter.cs` | No KPodman equivalent; new code |
| `io/PodExtractService.java` | 125 | `KPod.Core/Pods/PodExtractService.cs` | `IntConsumer` becomes `IProgress<int>` |
| `io/PodManifestParser.java` | 81 | `KPod.Core/Manifests/PodManifestParser.cs` | |
| `io/PodReportExporter.java` | 127 | `KPod.Core/Reports/PodReportExporter.cs` | Fixed-column `.inf`, CRLF endings |
| `io/PodIniMounter.java` | 114 | `KPod.Core/PodIni/PodIniMounter.cs` | Build on KPodman's `PodIniReader` / `PodIniWriter` / `PodIniText` |
| `io/RawImageDecoder.java` | 218 | `KPod.Core/Images/RawImageDecoder.cs` | Returns `int[]` ARGB, never a `Bitmap` |
| `ui/MainWindow.java` | 1471 | `KPod.Windows/UI/MainForm.cs` | The bulk of the work; see phase 5 |
| `ui/PreviewWindow.java` | 538 | `KPod.Windows/UI/PreviewForm.cs` | Plus `HexView.cs`, `ImageView.cs` |
| `ui/AudioPlayerDialog.java` | 97 | `KPod.Windows/UI/AudioPlayerForm.cs` + `Platform/WavePlayer.cs` | See decision D1 |
| `ui/BuildArchiveDialog.java` | 103 | `KPod.Windows/UI/BuildArchiveForm.cs` | |
| `ui/ExtractOptionsDialog.java` | 84 | `KPod.Windows/UI/ExtractOptionsForm.cs` | |
| `ui/SearchDialog.java` | 134 | `KPod.Windows/UI/SearchForm.cs` | |
| `ui/AboutDialog.java` | 39 | `KPod.Windows/UI/AboutForm.cs` | Copy KPodman's, change the strings |
| `resources/palettes/metalcr2.act` | - | `KPod.Core/Assets/metalcr2.act` | `EmbeddedResource`, loaded by name |
| `test/.../PodArchiveFormatTest.java` | 190 | `KPod.Tests/Pods/PodArchiveFormatTests.cs` | Ported test for test |

### Copied from KPodman without change

These files are already written, already ship in a released product, and solve
exactly the problems this port hits. Copy them, rename the namespace, do not
rewrite them.

| File | What it gives KPod |
|---|---|
| `Compat/IsExternalInit.cs` | `record` and `init` accessors on net48 |
| `Compat/Polyfills.cs` | `Index`, `Range`, and the small BCL gaps |
| `Compat/FrameworkExtensions.cs` | Framework-only extension methods, in scope via `<Using>` |
| `Compat/Json.cs` | 317 lines of dependency-free JSON read and write, for `config.json` |
| `Compat/PathCompat.cs` | `Path.GetRelativePath` and `TrimEndingDirectorySeparator` |
| `Compat/PodText.cs` | ISO-8859-1 by code page 28591, since net48 has no `Encoding.Latin1` |
| `Compat/OsCompat.cs`, `Compat/OrderedMap.cs` | Small gaps used by the preferences and list code |
| `KPodman.Windows/Program.cs` | Entry point shape, `#if NET5_0_OR_GREATER` startup split, fatal-error dialog |
| `KPodman.Windows/app.manifest` | DPI awareness on net48 |
| `KPodman.Windows/UI/ScreenFit.cs`, `Dialogs.cs`, `AppIcon.cs` | Window sizing, message-box wrappers, icon loading |
| `KPodman.Windows/Assets/build-icon.sh` | ICO generation from SVG |
| `Directory.Build.props`, `global.json`, `KPodman.slnx` | The build spec itself |

`KPodman.Core/Pods/PodArchiveReader.cs` is the closest thing to a head start
that exists: it is already a C# translation of the same Java reader, with the
same constants and the same EPD name-splitting logic. It implements only the
classic 40-byte POD1 directory, so KPod's copy needs the POD1-64 work from
section 5 grafted on.

---

## 5. Format work

JPod gained POD1-64 support before this port started, and KPod inherits it. The
extension is the Community Patch 3 long-name directory: the 84-byte header is
unchanged, the name field widens from 32 to 64 bytes, and each directory record
is therefore 72 bytes instead of 40.

| Property | Classic POD1 | POD1-64 |
|---|---:|---:|
| Header | 84 bytes | 84 bytes |
| Directory name field | 32 bytes | 64 bytes |
| Longest name | 31 bytes | 63 bytes |
| Directory record | 40 bytes | 72 bytes |
| Entry `i` starts at | `84 + i * 40` | `84 + i * 72` |

### Reading

POD1 has no magic value, so the layout is detected by validating it:

1. Reject `dtxe` (EPD) and `POD2` first, by signature.
2. Read the `int32` item count and the 80-byte comment.
3. Try the classic 40-byte directory. Accept it only if **every** record decodes
   to a non-empty path free of control characters and drive separators, no
   longer than 63 bytes, whose byte range lies inside the file.
4. If that fails, try the 72-byte directory with the same checks.
5. If neither validates, reject the archive.

Classic is tried first so an ordinary archive is never reported as extended.
Bounds are tested as `offset <= fileSize && length <= fileSize - offset`, never
`offset + length > fileSize`, which can overflow.

### Writing

Emit classic POD1 whenever every entry name fits in 31 bytes; switch to POD1-64
only when a name needs the wider field, because extended archives can only be
opened by updated engines and tools. The whole stored path counts, including any
`ART\` or `MODELS\` prefix, the extension, and the terminator. Names longer than
63 bytes are rejected rather than truncated. Names are NUL-terminated and their
fields zero-filled. The UI says which layout was written.

### Version tag: unresolved

The upstream engine notes describe production extended archives as "64-byte
names, version-tagged header", but the companion contract that would define the
tag bytes and offset has never been published. JPod, JSPod and JSTruckViewer all
implement the untagged 84-byte-header interpretation and validate it
structurally. KPod does the same. If a known-good tagged archive turns up, the
detection in all four projects changes together, driven by the observed bytes
rather than a guess. Keep any such archive as a test fixture.

### Cross-repository follow-up

`KPodman.Core/Pods/PodArchiveReader.cs` still reads only the classic directory,
so KPodman currently fails to open an extended POD that KPod writes. The same
detection change should land there. That is a KPodman change, tracked here only
so it is not forgotten.

---

## 6. Phases

Each phase ends in something that builds and is verified. Nothing is left half
ported across a phase boundary.

### Phase 0 - Skeleton

Create `Directory.Build.props`, `global.json`, `KPod.slnx`, and the three
project files, all copied from KPodman with names changed. `Directory.Build.props`
carries `Product` KPod, `Company` mtm2.com, `Authors` Kmaster, `Description`
"Terminal Reality POD archive utility for Windows", and `Version` 1.0.0.0.

Copy the whole `Compat/` folder. Add a single trivial test.

**Exit:** `dotnet build KPod.slnx` produces both target frameworks with no
warnings, and `dotnet test KPod.slnx -f net10.0` passes, on macOS.

### Phase 1 - Archive formats

Port `PodArchive`, `PodEntry`, `PodFormat`, `PodArchiveReader`, and
`PodArchiveWriter`. Start the reader from KPodman's, then add POD1-64 detection
and write the writer from JPod's. `PodFormatException` replaces `IOException`
for malformed archives, as in KPodman.

Port `PodArchiveFormatTest` test for test, then add the POD2 and EPD reader
tests from `KPodman.Tests/Pods/PodArchiveReaderTests.cs`.

**Exit:** the ported tests pass on both target frameworks. A real classic
archive (`FURYSE.POD`, 1 330 entries, longest name 19 bytes) reports `POD1` and
round-trips byte-identically through read then write. A synthetic extended
archive reports `Extended POD1` and preserves a 40-byte path.

### Phase 2 - Services

Port `PodExtractService`, `PodManifestParser`, `PodReportExporter`,
`PodIniMounter`, and the preferences layer. `IntConsumer` becomes
`IProgress<int>`. `PodIniMounter` builds on KPodman's `PodIniReader`,
`PodIniWriter` and `PodIniText`, which already handle the count-line format,
UTF-8 with CRLF and no BOM, and duplicate detection.

Preferences live at `%APPDATA%\KPod\config.json`. On first run, import
`%APPDATA%\JPod\config.json` if present, the way KPodman imports JPodman's, so
someone moving from the Java build keeps their recent-file list.

**Exit:** `.inf` and `.lst` output is byte-identical to JPod's for the same
archive, verified by diffing against files JPod generated. Note that a POD1-64
name can be wider than its `.inf` column, in which case the later columns shift
right rather than overwriting the name; the C# port must reproduce that
behaviour exactly.

### Phase 3 - Image decoding

Port `RawImageDecoder` into `KPod.Core/Images/`. Palette resolution keeps JPod's
six-step order: same-name `.ACT` in the same archive directory, then `VGA.ACT`,
then `METALCR2.ACT`, then any `.ACT` in the archive, then the embedded
`metalcr2.act`, then greyscale. Embed the palette as an `EmbeddedResource`.

The decoder returns `int[]` in ARGB, plus width and height. It never touches
`System.Drawing`.

**Exit:** decoding a known `.RAW` from a real archive produces the same pixel
array as JPod, compared as a hash over both outputs.

### Phase 4 - Shell

`Program.cs`, `app.manifest`, `AppIcon`, `Dialogs`, `ScreenFit`, `AboutForm`,
and an empty `MainForm` that opens an archive and lists its entries in a
`ListView`. Generate `KPod.ico` from an SVG with `build-icon.sh`.

**Exit:** `dotnet run --project KPod.Windows -f net48` on Windows opens a real
POD and shows its entries. `dotnet publish` produces a single exe.

### Phase 5 - Main window

The largest single piece of work: 1 471 lines of Swing become WinForms.

- `JTable` with a `DefaultTableModel` becomes a virtual-mode `ListView` in
  details view with Name, Size and Description columns. Virtual mode matters:
  Hellbender's `GAME.POD` has over 4 000 entries.
- Column-header sorting cycles name, size, description, then back to archive
  order on the third click, as JPod does.
- The collapsible folder tree, collapsed by default, is drawn as indented rows
  with folder icons, not a `TreeView`, to keep the single-list selection model.
- `SwingWorker` becomes `async`/`await` with `IProgress<int>`; every long
  operation keeps the busy state and progress label JPod shows.
- `JFileChooser` becomes `OpenFileDialog`, `SaveFileDialog` and
  `FolderBrowserDialog`.
- `java.awt.dnd` becomes `AllowDrop` with `DragEnter` / `DragDrop` and
  `DataFormats.FileDrop`.
- The quick-filter text box filters live; the advanced search dialog is phase 6.
- The comment field edits the 80-byte header comment.
- Recent files render into the File menu from `AppConfig`.

**Exit:** every feature in the JPod feature list works, checked against the
manual checklist. Saving an archive whose names exceed 31 bytes reports
`Extended POD1` in the status line and in the confirmation dialog.

### Phase 6 - Dialogs

`PreviewForm` with its type-aware routing (paletted RAW and CLR, ACT swatch
grid, standard images, text, hex dump of the first 4 096 bytes), the non-standard
RAW size chooser with its `64000 -> 320x200`, `256000 -> 640x400`,
`307200 -> 640x480` defaults and the swap-dimensions button, plus
`AudioPlayerForm`, `BuildArchiveForm`, `ExtractOptionsForm` and `SearchForm`.

**Exit:** every preview type in the JPod README renders, and the manual
checklist for previews passes.

### Phase 7 - Polish and release

Write `README.md` on KPodman's structure: badges, screenshot, features,
requirements, getting started, usage tables, building from source, project
layout, credits. Write `MANUAL-CHECKLIST.md`. Take `docs/main.jpg`. Verify both
publish commands. Tag 1.0.0.

**Exit:** both executables run on a clean Windows 10 VM with no runtime
installed beyond what ships with the OS, and the manual checklist passes end to
end.

---

## 7. Java to .NET Framework 4.8 mapping

| JPod uses | KPod uses | Watch out for |
|---|---|---|
| `java.nio.file.Path`, `Files` | `System.IO.Path`, `File`, `Directory` | `Path.GetRelativePath` is .NET Core only; use `PathCompat` |
| `StandardCharsets.ISO_8859_1` | `PodText.Latin1` | `Encoding.Latin1` does not exist on net48 |
| `record` | `record` | Needs `Compat/IsExternalInit.cs` on net48 |
| `List.copyOf`, `List.of` | `IReadOnlyList<T>`, `Array.Empty<T>()` | No immutable-collection package; copy into arrays |
| `Integer.toUnsignedLong` | `(long)(uint)` | Signed-shift bugs are the classic POD parsing failure |
| `Math.toIntExact` | `checked((int)...)` | Keep the overflow throw |
| `String.trim()` | `.Trim()` | Java's `trim` strips every code point at or below U+0020, control bytes included. `String.Trim()` uses `char.IsWhiteSpace`, which leaves U+0001 in place. This changes name decoding, so strip control characters explicitly or the plausibility check behaves differently in KPod than in JPod |
| `SwingWorker` | `async` / `await`, `IProgress<T>` | Marshal UI updates back with the captured context |
| `JTable` + `DefaultTableModel` | `ListView` in virtual mode | Non-virtual is unusably slow past a few thousand rows |
| `JFileChooser` | `OpenFileDialog` / `SaveFileDialog` / `FolderBrowserDialog` | `FolderBrowserDialog` differs between net48 and net10; guard the one property that changed, as KPodman does |
| `javax.imageio.ImageIO` | `System.Drawing.Bitmap` | Only in `KPod.Windows`; `Image.FromStream` needs the stream kept open |
| `BufferedImage TYPE_INT_ARGB` | `Bitmap` + `LockBits` with `Format32bppArgb` | Feed it the decoder's `int[]` directly |
| `javax.sound.sampled.Clip` | see decision D1 | |
| `java.awt.dnd` | `AllowDrop` + `DataFormats.FileDrop` | |
| `java.util.regex` | `System.Text.RegularExpressions` | |
| Hand-rolled JSON in `AppConfig` | `Compat/Json.cs` | Same output shape, so the config file stays compatible |
| JUnit 5 | xUnit | `@TempDir` becomes KPodman's `TempDir` helper |

---

## 8. Decisions

**D1 - WAV playback.** JPod's `AudioPlayerDialog` uses `javax.sound.sampled.Clip`
for play, pause, stop and a position readout. `System.Media.SoundPlayer`, the
only zero-dependency option in the box, plays and stops but cannot pause and
reports no position. NAudio would give all of it and costs the
no-NuGet-at-runtime rule, which is the whole point of the net48 target.

*Decision:* `KPod.Windows/Platform/WavePlayer.cs` wraps `winmm.dll` through
`mciSendString`, which gives play, pause, resume, stop and millisecond position
with no package. It needs a real file, so write the entry's bytes to a temp file
and delete it on close. Fall back to `SoundPlayer` if the MCI open fails, with
the pause button disabled.

**D2 - Image formats in preview.** GDI+ handles BMP, PNG, JPEG, GIF and TIFF,
which covers what `ImageIO` reads for these archives. No decision needed beyond
catching the `ArgumentException` GDI+ throws for anything it cannot read and
falling through to the hex dump, matching JPod's behaviour.

**D3 - High DPI.** The net10 leg sets `ApplicationHighDpiMode` to `PerMonitorV2`
in the csproj. The net48 leg has no such property and takes DPI awareness from
`app.manifest`, copied from KPodman. Both legs need testing at 150 %.

**D4 - Config location and name.** `%APPDATA%\KPod\config.json`, same schema as
JPod's, so the file is portable between them. Import from `%APPDATA%\JPod` on
first run.

**D5 - Entry-name case.** JPod uppercases manifest-derived names but writes
drag-and-dropped names as supplied. Reproduce exactly; do not "fix" it during
the port. Any change to this belongs in JPod first.

**D6 - Item-count ceilings.** JPod's reader accepts up to 8 192 entries and its
writer up to 4 096. That asymmetry is real and is preserved: it means JPod can
open Hellbender's `GAME.POD` but not rewrite it. Keep both numbers and revisit
after parity.

---

## 9. Testing

`KPod.Tests` targets `net48` and `net10.0` and runs on any OS, exactly like
`KPodman.Tests` and its 202 tests. Coverage targets, in priority order:

1. **Format round trips.** Classic and POD1-64 write then read, for every
   boundary: 31-byte name stays classic, 32-byte name forces extended, 63-byte
   name accepted, 64-byte name rejected.
2. **Detection.** Hand-built directories at both record widths; the classic
   layout wins when both could parse; truncated tables, empty name fields,
   control characters inside names, and out-of-range payloads are all rejected.
3. **Golden files.** `.inf` and `.lst` output diffed against files JPod
   produced from the same archive, including one with a name wider than the
   `.inf` name column.
4. **Services.** Extraction with and without folder structure, manifest parsing
   including the `filename,archiveName` form, `pod.ini` mounting including the
   duplicate and over-limit paths.
5. **Decoding.** Palette resolution order, ACT parsing, dimension detection and
   the suggestion list.

Everything the tests cannot reach goes in `MANUAL-CHECKLIST.md`, written on
KPodman's model: startup, lists, sorting, filtering, drag and drop, each preview
type, save and extract, DPI at 100 % and 150 %, and SmartScreen behaviour on
first run of the unsigned exe.

---

## 10. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| `MainForm` is 1 471 lines of Swing | The port stalls in phase 5 | Phase 4 ships a working shell first, so phase 5 is incremental against a running app |
| `String.Trim()` and `String.trim()` differ on control characters | Silent divergence in name decoding between JPod and KPod | Called out in the mapping table; covered by a dedicated test |
| `ListView` performance on 4 000+ entries | Unusable on Hellbender's `GAME.POD` | Virtual mode from the start, not retrofitted |
| No tagged POD1-64 fixture exists | A production C-Pod archive might not open | Structural validation only, documented as unresolved, fixture kept if one appears |
| WinForms differences between net48 and net10 | One leg breaks silently | Both legs build in CI; the manual checklist is run against the net48 exe, which is the shipping one |
| KPodman's reader lacks POD1-64 | An archive KPod writes will not mount | Tracked as a cross-repository follow-up in section 5 |
| Scope creep during the port | Parity never arrives | No feature additions before 1.0.0; new ideas go in an issue |

---

## 11. Definition of done

- `dotnet build KPod.slnx` is clean on both target frameworks, on macOS and
  Windows.
- `dotnet test KPod.slnx -f net10.0` and `-f net48` both pass.
- `dotnet publish KPod.Windows -c Release -f net48 -p:DebugType=none` yields a
  single exe that runs on a clean Windows 10 install with no runtime downloaded.
- Every feature in the JPod README works, verified against
  `MANUAL-CHECKLIST.md`.
- A classic archive round-trips byte-identically; an extended archive preserves
  every path in full and is reported as `Extended POD1`.
- `README.md` follows the KPodman structure and states the .NET Framework 4.8
  requirement plainly.

---

## Reference build commands

```sh
dotnet restore KPod.slnx
dotnet build   KPod.slnx                 # builds net48 and net10.0 together
dotnet test    KPod.slnx -f net10.0
dotnet run --project KPod.Windows -f net48   # Windows only

# .NET Framework 4.8, the main download
dotnet publish KPod.Windows -c Release -f net48 -p:DebugType=none

# .NET 10, the alternative
dotnet publish KPod.Windows -c Release -f net10.0-windows -r win-x64 \
  --self-contained false -p:PublishSingleFile=true -p:DebugType=none
```

---

## References

- [JPod](../JPod) - the Java 17 original this port follows
- [KPodman](../KPodman) - the build, compat and packaging template used throughout
- [JSPod POD1-64 format notes](../JSPod/docs/POD1_64_FORMAT.md) - the description
  of the extended directory, and the record of what is still unknown
- [MTM2 Engine Content Limits](https://www.mtm2.com/~mtmg/misc/ENGINE_LIMITS.md)
