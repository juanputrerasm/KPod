# KPod manual GUI checklist

Everything below needs a real Windows machine and a few real POD archives. The
non-UI behaviour is covered by `dotnet test`; this list only covers what automated
tests cannot reach.

Run from anywhere:

```
dotnet run --project KPod.Windows -f net48
```

Useful archives to have on hand: a classic POD1 with subfolders (any MTM track
pack), a large one (Hellbender `GAME.POD`, 4 000+ entries), a POD2 (4x4 Evo), an
EPD (Fly!), and one saved by KPod itself with a name longer than 31 characters.

## Startup

- [ ] The window opens with an empty list, the title reads `KPod`, and the status
      square at the right edge is green.
- [ ] `KPod.exe <path-to-pod>` on the command line opens that archive directly.
- [ ] Dragging a `.pod` onto `KPod.exe` in Explorer opens it.
- [ ] The taskbar and title-bar icons are the crimson KPod tile, not the generic
      application icon.
- [ ] First run on a machine with `%APPDATA%\JPod\config.json` present shows
      JPod's recent files under File > Open Recent.

## Opening and browsing

- [ ] A classic POD opens, the title reads `KPod - NAME.POD (POD1)`, and the
      status line reports the entry count.
- [ ] A POD2 archive reads `(POD2)`; an EPD reads `(EPD)`.
- [ ] An archive saved with a long name reads `(Extended POD1)`.
- [ ] Folders show a folder icon, start collapsed, and sit above the loose files.
- [ ] Double-clicking a folder row expands it; double-clicking again collapses it.
      The row stays selected across the toggle.
- [ ] Nested folders indent one step per level.
- [ ] **Expand +** opens everything; **Collapse -** closes everything.
- [ ] Hellbender's `GAME.POD` opens and scrolls smoothly. Scrolling through all
      4 000+ entries has no visible stutter and no blank rows.
- [ ] The comment field shows the archive's 80-character comment.
- [ ] `Size` in the status bar matches the file size on disk for a freshly opened
      archive.

## Sorting and filtering

- [ ] Clicking `Name` sorts ascending and the header gains an up arrow; clicking
      again sorts descending; a third click restores archive order and drops the
      arrow.
- [ ] The same three-click cycle works on `Size` and `Description`.
- [ ] Under any sort, folder rows stay above the files in their level.
- [ ] Typing in the filter box narrows the list as you type.
- [ ] A filter reveals matches inside collapsed folders without expanding them by
      hand; clearing the filter puts the collapse state back.
- [ ] Filtering on a folder name keeps that folder's whole subtree.

## Selection

- [ ] Ctrl-click and Shift-click both extend the selection, and `Selected` in the
      status bar tracks it.
- [ ] Selecting a folder heading counts every entry underneath it, including
      entries in nested folders.
- [ ] The selection survives expanding, collapsing, sorting and filtering.

## Preview

- [ ] A 64x64 `.raw` opens as an image at 4x with hard pixel edges, no blur.
- [ ] A 256x256 `.raw` or `.clr` opens at 1:1.
- [ ] A `.raw` whose size is not square (try a 64 000-byte one) opens the
      dimensions dialog with `320 x 200` preselected.
- [ ] In that dialog, picking a suggested size fills width and height; **Swap**
      exchanges them; entering a width and height whose product does not match the
      payload is refused with a message rather than drawn.
- [ ] Cancelling the dimensions dialog opens no preview window at all.
- [ ] The palette dropdown lists the same-name ACT first when one exists, then
      VGA, the archive's other ACTs, METALCR2, greyscale and the bundled palette.
      Switching palette visibly changes the image.
- [ ] An `.act` opens as a 16x16 swatch grid, and hovering a swatch shows
      `Index n - #RRGGBB`.
- [ ] A `.txt`, `.sit` or `.trk` opens in the monospaced text view, scrolled to
      the top.
- [ ] A `.bmp` or `.png` opens as an image.
- [ ] A `.bin` or other binary opens as a hex dump, 16 bytes per line with the
      ASCII gutter, capped at 4 096 bytes and saying so in the first line.
- [ ] Pressing Enter on a selected file opens the same preview as double-clicking.

## Audio

- [ ] A `.wav` opens the audio player directly; no empty preview window flashes
      first.
- [ ] **Play** starts playback and the position readout counts up.
- [ ] **Pause** stops the audio and the readout freezes; the button becomes
      **Resume** and pressing it continues from the same position.
- [ ] **Stop** halts playback and returns the position to zero.
- [ ] Closing the player stops the audio immediately.
- [ ] `%TEMP%` holds no leftover `kpodwave*.wav` after the player is closed.
- [ ] A malformed or non-PCM WAV shows "Cannot play this audio file" and leaves
      the transport buttons disabled instead of throwing.

## Editing

- [ ] **Add Files...** appends the picked files, and the status line reports how
      many were added.
- [ ] Dropping files onto the entry list adds them the same way.
- [ ] The `unsaved` marker appears after any edit and clears after a successful
      save.
- [ ] **Remove** drops the selection; removing a folder heading removes every
      entry under it.
- [ ] **Replace with File...** on one entry swaps its data and keeps its archive
      name; the size column updates.
- [ ] Replace with no selection, or with more than one, warns rather than acting.
- [ ] Editing the comment field marks the archive unsaved.

## Saving

- [ ] **Save As...** on an archive whose names all fit in 31 characters writes a
      file whose status line says `(POD1)`.
- [ ] Reopening that file reports `POD1` and lists the same entries.
- [ ] Adding a file whose name exceeds 31 characters and saving reports
      `(Extended POD1)` in the status line and shows the explanatory dialog.
- [ ] Reopening that file reports `Extended POD1` and the long name survives in
      full.
- [ ] `Size` in the status bar grows by 32 bytes per entry the moment the archive
      tips over into the extended layout.
- [ ] Trying to save an entry whose name is longer than 63 characters fails with a
      message naming the entry, and writes nothing.
- [ ] Save As appends `.pod` when the typed name has no extension.

## Extraction

- [ ] **Extract All** recreates the archive's folder structure under the chosen
      destination.
- [ ] **Extract Sel.** opens the options dialog; with **Preserve folder structure**
      ticked the subfolders are recreated, and unticked every file lands flat in
      the destination.
- [ ] The status line counts progress during a large extraction and reads
      `Extraction complete.` at the end.
- [ ] Extracting from an Extended POD1 archive recreates the full long path.
- [ ] Extracting to a read-only or missing destination reports the error rather
      than failing silently.

## Reports

- [ ] **Save .inf Report** produces the fixed-column report: header lines, a blank
      line, the column header, then one line per entry with the size at column 30
      and the offset at column 45.
- [ ] The file starts with the letter `P`, not a byte-order mark. Check in a hex
      editor.
- [ ] For an Extended POD1 archive, a name wider than 30 characters pushes the
      size and offset right instead of overwriting the name.
- [ ] **Save .lst List** produces one entry name per line, and that file loads
      back through File > Open Response List File.
- [ ] Save .inf on a manifest-loaded list warns that a POD must be open first.

## Response lists

- [ ] Open Response List File loads a `.lst` and reports how many entries came in.
- [ ] The `filename,archiveName` form stores the entry under the second name.
- [ ] A `.lst` naming a file that is not on disk reports which one is missing.
- [ ] A manifest load marks the archive unsaved immediately, since nothing has
      been written yet.

## pod.ini

- [ ] Tools > Mount in pod.ini adds the open archive to `pod.ini` in its own
      folder, bumping the count on the first line.
- [ ] With no `pod.ini` beside the archive, the one in the parent folder is used.
- [ ] Mounting the same POD twice reports "POD already mounted".
- [ ] With no `pod.ini` anywhere near, the message is "File POD.INI cannot be
      located."
- [ ] Mounting into a list of 99 entries warns about the recommended size and
      still mounts.
- [ ] No `pod.wrk` is left in the folder afterwards.

## Search

- [ ] The window is titled `Search`, carries the KPod icon, is modal, and does
      not appear in the taskbar.
- [ ] Search finds entries by name substring and reports the match count.
- [ ] **Sizes** matches on the byte count; **Match case** makes the search
      case-sensitive.
- [ ] **Find Next** on a fresh query selects the first match and reports
      `Match 1 of n`; pressing it again steps forward and wraps at the end.
- [ ] **Find Previous** steps backward from the current match and wraps at the
      start. On a fresh query it selects the last match.
- [ ] Editing the query or toggling any option re-runs the search on the next
      **Find Next**, rather than stepping through stale results.
- [ ] Each step selects the entry in the main window behind the dialog.
- [ ] Double-clicking a result, or **Jump To**, selects that entry in the main
      window, expanding whatever folders it takes to reveal it.
- [ ] All four buttons are the same width and the window has no wasted space
      around the result list.

## Dialog chrome

- [ ] Every window other than the main one carries the crimson KPod icon: About,
      Search, Preview, Audio Player, Make Archive, Extract, RAW Dimensions,
      POD2 Audit History, and the folder and rename prompts.
- [ ] Every one of those is modal: the main window cannot be clicked until it is
      closed, and none of them appear in the taskbar.
- [ ] Sub-window titles carry no `KPod - ` prefix. They read `Search`,
      `Make Archive`, `Extract`, `RAW Dimensions`, `POD2 Audit History`,
      `About KPod`, `Preview - NAME` and `Audio Player - NAME`. Only the main
      window is titled `KPod - NAME.POD (FORMAT)`.
- [ ] Closing the Audio Player mid-playback stops the sound and releases the
      device; reopening it plays again from the start.

## Recent files

- [ ] Opening archives fills File > Open Recent, newest first, with no duplicates.
- [ ] The list stops at ten entries.
- [ ] Choosing a recent file whose path no longer exists warns and drops it from
      the list.
- [ ] The list survives a restart, and `%APPDATA%\KPod\config.json` holds it.

## Unsaved changes

- [ ] With unsaved edits, Open, New Archive, Open Response List File and closing
      the window all ask before discarding.
- [ ] Answering No cancels the action and keeps the edits.

## Display

- [ ] At 150% scaling the toolbar, list, status bar and every dialog lay out
      correctly with no clipped text.
- [ ] Moving the window between monitors with different scaling keeps it sharp
      (net48 leg, which takes DPI awareness from the manifest).
- [ ] On a 1366x768 laptop the window opens fully on screen.
- [ ] Resizing the window keeps the list filling the space, with the comment and
      filter rows fixed at the top.
