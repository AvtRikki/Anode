# Anode

A desktop ECAD application in C# / .NET 10 and Avalonia 12, built on the KiCad file formats.

The application is a **plugin platform**: a workbench (Kicad·One) that knows about documents, docks, commands and
themes, and domain plugins that bring the actual editors. Two plugins exist today: **PCB** (`.kicad_pcb`, viewing and
basic editing) and **schematic** (`.kicad_sch`, viewing), for KiCad 8, 9 and 10. Files are round-tripped losslessly:
opening and saving without edits reproduces the original bytes.

## Layout

| Project | Purpose |
|---|---|
| **core** — file formats and the typed model, no UI | |
| `src/core/Anode.Sexpr` | Lossless S-expression parser and writer (CST that keeps whitespace), plus a port of KiCad's formatter |
| `src/core/Anode.Geometry` | Nanometre integer geometry, transforms, arcs |
| `src/core/Anode.Kicad` | Typed model as views over the CST: boards (layers, nets, footprints, pads, tracks, zones) and schematics (symbols, wires, labels, sheets) |
| `src/core/Anode.Editing` | Board editing over the CST: selection, move, rotate, delete, undo/redo |
| **render** — drawing, backend by backend | |
| `src/render/Anode.Render` | Backend-agnostic scenes (per-layer primitives), camera, hit testing, stroke fonts, layer palette |
| `src/render/Anode.Render.Skia` | SkiaSharp renderer (see `docs/adr/0001-renderer.md`) |
| `src/render/Anode.Render.OpenGl` | OpenGL renderer, the default |
| `src/render/Anode.Render.Avalonia` | The Skia and OpenGL surfaces the plugins draw their canvases on |
| **app** — the workbench and what plugins may use | |
| `src/app/Anode.Sdk` | Plugin SDK: `IPlugin`, commands, panels, documents, translations, theme keys, UI kit helpers |
| `src/app/Anode.Workbench` | The workbench: title bar, docks and icon rail, document tabs and split, command palette, status bar |
| **plugins** — one domain each | |
| `src/plugins/Anode.Plugin.Pcb` | PCB: `.kicad_pcb` documents, board canvas, layers panel, board commands |
| `src/plugins/Anode.Plugin.Schematic` | Schematic: `.kicad_sch` sheets, sheet canvas, sheets panel |
| `tests/core`, `tests/render`, `tests/app` | xUnit v3 tests, laid out like `src` |

## Requirements

- .NET SDK 10.0.401 or later (pinned in `global.json`)

## Build and test

```bash
./tools/fetch-fixtures.sh
```

```bash
dotnet build Anode.slnx
```

```bash
dotnet test --solution Anode.slnx
```

`fetch-fixtures.sh` downloads KiCad demo and QA boards (about 176 MB) into `test-data/`, which is not committed.
Without them the fixture tests are skipped.
The offscreen render tests write PNGs to `test-output/renders/`, and the headless workbench tests write window
snapshots next to their binaries in `snapshots/` (override with `ANODE_SNAPSHOT_DIR`).

## Languages

The interface is English by default and ships with Russian. Texts live in JSON catalogs embedded in each assembly
(`src/app/Anode.Workbench/i18n/en.json`, `src/plugins/Anode.Plugin.Pcb/i18n/ru.json`, …); a plugin registers its own on activation:

```csharp
Tr.Register(JsonTextCatalog.FromAssembly(Assembly.GetExecutingAssembly()));
```

Code asks for keys, never for literals — `Tr.T("command.file.open")`, `Tr.Plural("pcb.layer", count)` — and command
and panel descriptors carry `TitleKey`/`RailLabelKey`, so a language switch retitles everything that is already on
screen. A missing key falls back to English and finally to the key itself, so a half-translated plugin still works.

Adding a language is adding one file, `i18n/<culture>.json`, next to the others: the workbench finds it, lists it in
the command palette ("Language: …") and remembers the choice. The language can also be set at startup:

```bash
dotnet run --project src/app/Anode.Workbench -- --lang=ru
```

## Plugins

The workbench itself knows nothing about boards. Domain features are plugins: a folder under `plugins/` with a
`plugin.json` manifest next to the plugin's assemblies.

```json
{
  "id": "anode.pcb",
  "name": "Плата KiCad",
  "version": "0.1.0",
  "assembly": "Anode.Plugin.Pcb.dll",
  "entryType": "Anode.Plugin.Pcb.PcbPlugin",
  "contractVersion": 1
}
```

Each plugin is loaded into its own `AssemblyLoadContext`; assemblies the host already ships (the SDK, Avalonia,
SkiaSharp) resolve from the default context so types are shared, everything else comes from the plugin folder.
A plugin whose `contractVersion` differs from `PlatformContract.Version` is refused. Its entry type implements
`IPlugin` and, from `IPluginContext`, registers document types, panels and commands.

`dotnet build` copies both plugins into `src/app/Anode.Workbench/bin/<configuration>/net10.0/plugins/`.

## Run

```bash
dotnet run --project src/app/Anode.Workbench -- path/to/board.kicad_pcb
```

The renderer is chosen at startup. OpenGL is the default; the Skia fallback is selected like this:

```bash
dotnet run --project src/app/Anode.Workbench -- --renderer=skia path/to/board.kicad_pcb
```

The `ANODE_RENDERER=skia` environment variable does the same. See `docs/adr/0001-renderer.md` for the comparison.

Viewing:

- Wheel: zoom around the cursor. Shift + wheel: pan horizontally.
- Middle or right drag, or Space + left drag: pan.
- Home: zoom to fit. ⌘O: open. Files can also be dropped onto the window.

Editing (footprints, tracks, arcs, vias, board graphics, texts and zones):

- Click: select; Shift+click adds or removes. Clicking a pad selects its footprint and highlights the pad's net.
- Drag from empty space: box selection. Left-to-right selects enclosed items, right-to-left everything touched.
- Drag a selected item, or press M and click to place: move with the anchor snapped to a 0.1 mm grid.
- R rotates 90° counter-clockwise (Shift+R clockwise), also while moving. Delete or Backspace deletes.
- Esc cancels a move or clears the selection.
- ⌘Z undo, ⌘⇧Z redo, ⌘S save, ⌘⇧S save as. Undoing everything restores the file byte for byte, and closing
  or opening another board asks about unsaved changes.

On macOS the window needs an active display. With the lid closed and no external monitor,
Avalonia cannot start its render timer.
