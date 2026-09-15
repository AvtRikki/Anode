# Ecad

A desktop ECAD application in C# / .NET 10 and Avalonia 12, built on the KiCad file formats.

The current milestone is a **PCB viewer** for KiCad 8, 9 and 10 `.kicad_pcb` files. Files are round-tripped
losslessly: opening and saving without edits reproduces the original bytes.

## Layout

| Project | Purpose |
|---|---|
| `src/Ecad.Sexpr` | Lossless S-expression parser and writer (CST that keeps whitespace), plus a port of KiCad's formatter |
| `src/Ecad.Geometry` | Nanometre integer geometry, transforms, arcs |
| `src/Ecad.KiCad` | Typed board model as views over the CST: layers, nets, footprints, pads, tracks, vias, zones, graphics, text |
| `src/Ecad.Rendering` | Backend-agnostic scene (per-layer primitives), camera, hit testing, layer palette |
| `src/Ecad.Rendering.Skia` | SkiaSharp renderer (prototype A, see `docs/adr/0001-renderer.md`) |
| `src/Ecad.App` | Avalonia viewer: canvas, layer panel, properties panel |
| `tests/*` | xUnit v3 tests |

## Requirements

- .NET SDK 10.0.401 or later (pinned in `global.json`)

## Build and test

```bash
./tools/fetch-fixtures.sh
```

```bash
dotnet build Ecad.slnx
```

```bash
dotnet test --solution Ecad.slnx
```

`fetch-fixtures.sh` downloads KiCad demo and QA boards (about 176 MB) into `test-data/`, which is not committed.
Without them the fixture tests are skipped.
The offscreen render tests write PNGs to `test-output/renders/`.

## Run

```bash
dotnet run --project src/Ecad.App -- path/to/board.kicad_pcb
```

The renderer is chosen at startup. OpenGL is the default; the Skia fallback is selected like this:

```bash
dotnet run --project src/Ecad.App -- --renderer=skia path/to/board.kicad_pcb
```

The `ECAD_RENDERER=skia` environment variable does the same. See `docs/adr/0001-renderer.md` for the comparison.

- Wheel: zoom around the cursor. Shift + wheel: pan horizontally.
- Middle or right drag, or Space + left drag: pan.
- Click: select an item and highlight its net. Esc clears the selection.
- Home: zoom to fit. ⌘O: open. ⌘⇧S: save as.
- Files can be dropped onto the window.

On macOS the window needs an active display. With the lid closed and no external monitor,
Avalonia cannot start its render timer.
