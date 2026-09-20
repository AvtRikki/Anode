# Anode

A desktop ECAD application in C# / .NET 10 and Avalonia 12, built on the KiCad file formats.

Anode is a **plugin platform**: a workbench that knows about documents, docks, commands, panels and themes, and
domain plugins that bring the editors. Two plugins ship with it — **PCB** (`.kicad_pcb`) and **schematic**
(`.kicad_sch`) — for KiCad 8, 9 and 10. Files round-trip losslessly: opening and saving without edits reproduces the
original bytes, and undoing an edit gives the file back byte for byte.

What it does today, in short:

- **Boards:** open, look at and edit — select, move, rotate, delete, undo/redo, save. Layers panel, net highlighting,
  an inspector that describes the board or the selected item.
- **Schematics:** open and edit — drawing tools (wires, buses, labels, graphics, text), placing parts from symbol
  libraries, per-place designators on a reused sheet, annotation, nets of a sheet and of a whole design, checks.
- **Both:** KiCad's drawing sheets (frame and title block, a project's own `.kicad_wks` included), text in the stroke
  font or in any typeface the machine or the file itself carries, the title block written from the inspector.

The documentation lives in [docs/](docs/) — start at [docs/README.md](docs/README.md).

## Requirements

- .NET SDK 10.0.401 or later (pinned in `global.json`)

## Build, test, run

```bash
./tools/fetch-fixtures.sh
```

```bash
dotnet build Anode.slnx
```

```bash
dotnet test --solution Anode.slnx
```

```bash
dotnet run --project src/app/Anode.Workbench -- path/to/board.kicad_pcb
```

`fetch-fixtures.sh` downloads KiCad demo and QA files (about 176 MB) into `test-data/`, which is not committed.
Without them the fixture tests are skipped rather than failing. More on the tests, snapshots and fixtures in
[docs/testing.md](docs/testing.md).

On macOS the window needs an active display: with the lid closed and no external monitor, Avalonia cannot start its
render timer.

## Layout

| Project | Purpose |
|---|---|
| **core** — file formats and the typed model, no UI | |
| `src/core/Anode.Sexpr` | Lossless S-expression parser and writer (a CST that keeps whitespace), plus a port of KiCad's formatter |
| `src/core/Anode.Geometry` | Nanometre integer geometry, transforms, arcs, triangulation, region booleans |
| `src/core/Anode.Kicad` | The typed model as views over the CST — boards, schematics, symbol libraries, drawing sheets, embedded files, project files — with the writes, commands and undo stack over it |
| `src/core/Anode.Editing` | The editors the canvases drive: selection, move, rotate, delete, tools, save |
| **render** — drawing, backend by backend | |
| `src/render/Anode.Render` | Backend-agnostic scenes (per-layer primitives), camera, hit testing, fonts, drawing sheets, layer palette |
| `src/render/Anode.Render.Skia` | SkiaSharp renderer |
| `src/render/Anode.Render.OpenGl` | OpenGL renderer, the default ([why](docs/adr/0001-renderer.md)) |
| `src/render/Anode.Render.Avalonia` | The Skia and OpenGL surfaces the plugins draw their canvases on |
| **app** — the workbench and what plugins may use | |
| `src/app/Anode.Sdk` | Plugin SDK: `IPlugin`, commands, panels, documents, inspector rows, translations, theme keys, the icon pack and UI kit helpers |
| `src/app/Anode.Workbench` | The workbench: title bar, docks and icon rail, document tabs and split, command palette, project tree, inspector, checks, console, status bar |
| **plugins** — one domain each | |
| `src/plugins/Anode.Plugin.Pcb` | PCB: `.kicad_pcb` documents, board canvas, layers panel, board commands |
| `src/plugins/Anode.Plugin.Schematic` | Schematic: `.kicad_sch` sheets, sheet canvas, symbols panel, nets panel, sheet commands |
| `tests/…` | xUnit v3 tests, laid out like `src` |

## Plugins

The workbench knows nothing about boards or sheets. Domain features are plugins: a folder under `plugins/` with a
`plugin.json` manifest next to the plugin's assemblies.

```json
{
  "id": "anode.pcb",
  "name": "KiCad board",
  "version": "0.1.0",
  "assembly": "Anode.Plugin.Pcb.dll",
  "entryType": "Anode.Plugin.Pcb.PcbPlugin",
  "contractVersion": 14
}
```

Each plugin is loaded into its own `AssemblyLoadContext`; assemblies the host already ships (the SDK, Avalonia,
SkiaSharp) resolve from the default context so types are shared, everything else comes from the plugin folder. A
plugin whose `contractVersion` differs from `PlatformContract.Version` is refused. `dotnet build` copies both
plugins into `src/app/Anode.Workbench/bin/<configuration>/net10.0/plugins/`.

What a plugin may contribute, and how the pieces fit, is in [docs/architecture.md](docs/architecture.md).

## Languages

The interface is English by default and ships with Russian. Texts live in JSON catalogs embedded in each assembly
(`src/app/Anode.Workbench/i18n/en.json`, `src/plugins/Anode.Plugin.Pcb/i18n/ru.json`, …); a plugin registers its own
on activation:

```csharp
Tr.Register(JsonTextCatalog.FromAssembly(Assembly.GetExecutingAssembly()));
```

Code asks for keys, never for literals — `Tr.T("command.file.open")`, `Tr.Plural("pcb.layer", count)` — and command
and panel descriptors carry `TitleKey`/`RailLabelKey`, so a language switch retitles everything already on screen. A
missing key falls back to English and finally to the key itself, so a half-translated plugin still works.

Adding a language is adding one file, `i18n/<culture>.json`, next to the others: the workbench finds it, lists it in
the command palette ("Language: …") and remembers the choice. It can also be set at startup:

```bash
dotnet run --project src/app/Anode.Workbench -- --lang=ru
```

## Renderer

OpenGL is the default; the Skia renderer is chosen with `--renderer=skia` or `ANODE_RENDERER=skia`. Both draw the
same scenes, so the picture is the same either way. The comparison that decided it is in
[docs/adr/0001-renderer.md](docs/adr/0001-renderer.md).

## Licence

GPL-3.0-or-later; see [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
