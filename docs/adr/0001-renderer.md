# ADR 0001: Board renderer

Status: **in progress**. Prototype A (Skia) is implemented; prototype B (OpenGL) has not been built yet.

## Context

The viewer must draw large KiCad boards with smooth pan and zoom. The largest fixture,
`jetson-agx-thor-baseboard.kicad_pcb` (85 MB), produces about 446k primitives.

Rendering sits behind a backend-agnostic display list:

- `Ecad.Rendering.SceneBuilder` turns a `Board` into per-layer `LinePrim`, `CirclePrim`, `PolygonPrim` and `TextPrim`
  in scene millimetres (centred on the board outline, `float` precision).
- `Camera2D` maps scene coordinates to screen pixels.
- `HitTester` picks the topmost item; zones are only picked when nothing else is hit.

## Prototype A: SkiaSharp (`Ecad.Rendering.Skia`)

- One cached `SKPath` per layer and stroke width, one for circles, one for polygons.
- Semi-transparent layers are composited with `SaveLayer`, so overlapping primitives don't stack alpha.
- Selection and net highlight redraw the matching owners' primitives brightened over a dimmed board.
- Text is drawn in screen space with a system font and skipped below 4 px height.
- In the app it runs inside an Avalonia `ICustomDrawOperation` through `ISkiaSharpApiLeaseFeature`.

### Measurements

Measured on an Apple M4, Debug build, offscreen CPU raster at 1600×1000 with the whole board fitted
(`tests/Ecad.Rendering.Skia.Tests`):

| Board | File size | Parse | Scene build | Primitives | First frame | Cached frame |
|---|---:|---:|---:|---:|---:|---:|
| jetson-agx-thor-baseboard | 85 MB | 2.7 s | 0.61 s | 446,218 | 149 ms | 117 ms |
| vme-wren | 70 MB | 1.8 s | 0.33 s | 167,911 | 200 ms | 136 ms |
| tinytapeout-demo | 4.5 MB | 144 ms | 39 ms | 19,596 | 93 ms | 65 ms |
| video | 5.8 MB | 153 ms | 49 ms | 25,438 | 62 ms | 33 ms |
| pic_programmer | 0.6 MB | 28 ms | 7 ms | 5,120 | 20 ms | 15 ms |

GPU frame times inside the app window are still to be measured. The first attempt could not open
a window because the laptop lid was closed, so `CVDisplayLink` had no active display.

## Still to do before the decision

1. Measure GPU (Metal) frame times in the app while panning and zooming the two large boards.
2. Build prototype B: `OpenGlControlBase` with instanced SDF segments and circles plus triangulated polygons.
3. Compare frame time, memory and anti-aliasing quality at small zoom, then record the decision here.

Things either backend will probably need for large boards:

- Level-of-detail: skip primitives smaller than a pixel, and draw zones and pads as simple rectangles when zoomed out.
- A spatial index for culling and hit-testing (the hit tester is currently a linear scan).
- KiCad's stroke font (newstroke) instead of a system font, once its license is confirmed.
