# ADR 0001: Board renderer

Status: **accepted — OpenGL is the default renderer; Skia is kept as a fallback (`--renderer=skia`).**

Decided on 2026-09-15 after running both backends in the app window on the Jetson board (85 MB, 446k primitives):
Skia lagged heavily while panning and zooming, OpenGL stayed smooth.

## Context

The viewer must draw large KiCad boards with smooth pan and zoom. The largest fixture,
`jetson-agx-thor-baseboard.kicad_pcb` (85 MB), produces about 446k primitives.

Rendering sits behind a backend-agnostic display list:

- `Anode.Render.SceneBuilder` turns a `Board` into per-layer `LinePrim`, `CirclePrim`, `PolygonPrim` and `TextPrim`
  in scene millimetres (centred on the board outline, `float` precision).
- `Camera2D` maps scene coordinates to screen pixels; `ViewState` is the per-frame snapshot a backend draws.
- `HitTester` picks the topmost item; zones are only picked when nothing else is hit.

The app chooses the backend at startup: `--renderer=skia|opengl` or `ANODE_RENDERER`.

## Prototype A: SkiaSharp (`Anode.Render.Skia`)

- One cached `SKPath` per layer and stroke width, one for circles, one for polygons.
- Semi-transparent layers are composited with `SaveLayer`, so overlapping primitives don't stack alpha.
- Selection and net highlight redraw the matching owners' primitives brightened over a dimmed board.
- Text is drawn in screen space with a system font and skipped below 4 px height.
- In the app it runs inside an Avalonia `ICustomDrawOperation` through `ISkiaSharpApiLeaseFeature`.

## Prototype B: OpenGL 3.3 / ES 3.0 (`Anode.Render.OpenGl`)

- Silk.NET bindings over the context from Avalonia's `OpenGlControlBase` (or CGL in tests).
- Segments and discs are instanced quads with signed-distance fragment shaders: round caps and one-pixel
  anti-aliasing with no tessellation. Minimum width is clamped to one device pixel.
- Polygons are triangulated with `Earcut` (port of mapbox/earcut, no hole rings: KiCad fills are already fractured).
  `SceneTriangulator` does this in parallel on a background thread when the file is opened.
- One upload per layer: instance buffers for segments and discs, an indexed triangle buffer for fills.
- Draw calls per frame: up to three per visible layer, plus the grid, plus the highlight pass.
- Grid lines are regenerated per frame into a dynamic instance buffer.
- Shader dialect is chosen from the context: GLSL 330 core, 150, or 300 es.
- On macOS Avalonia is switched to its OpenGL compositor, because `OpenGlControlBase` cannot share with Metal.

Not in the prototype yet: text, layer group transparency (overlaps on mask and paste layers stack alpha),
and MSAA on polygon edges.

## Measurements

Apple M4, Debug build, 1600×1000, whole board fitted.

- **Skia:** offscreen CPU raster, `tests/render/Anode.Render.Skia.Tests`.
- **OpenGL:** headless CGL 4.1 context on Metal, 60 frames of zoom and pan, each timed with `glFinish`,
  `tests/render/Anode.Render.OpenGl.Tests`.

| Board | Primitives | Skia cached frame (CPU) | OpenGL median / p95 (GPU) | OpenGL first frame | Triangulation (background) | GPU buffers |
|---|---:|---:|---:|---:|---:|---:|
| jetson-agx-thor-baseboard | 446,218 | 117 ms | 2.9 / 9.0 ms | 54 ms | 1.1 s | 20.3 MB |
| vme-wren | 167,911 | 136 ms | 2.4 / 5.1 ms | 38 ms | 2.8 s | 20.8 MB |
| video | 25,438 | 33 ms | 0.7 / 1.4 ms | 11 ms | 0.3 s | 3.6 MB |
| tinytapeout-demo | 19,596 | 65 ms | 0.9 / 3.6 ms | 13 ms | 83 ms | 2.4 MB |
| pic_programmer | 5,120 | 15 ms | 0.3 / 1.2 ms | 17 ms | — | 0.3 MB |

Triangulation was measured while other tests ran in parallel. Single-threaded Release Earcut takes about
7–8 µs per vertex; vme-wren has 24 zone fills of 80k–167k vertices each (6.2 s sequentially).

Before triangulation moved off the render thread, the first GL frame took 3.7 s (Jetson) and 8.7 s (vme-wren).

The Skia column is CPU rasterisation, so it is not a like-for-like GPU comparison. Skia on Avalonia's
Metal backend still has to tessellate every path per frame. OpenGL does its geometry work once, at upload.

## Decision

OpenGL, for three reasons:
- It keeps large boards at a few milliseconds per frame, with headroom for more layers.
- Its cost does not grow with path complexity.
- The same shaders run on ES 3.0, via ANGLE on Windows.

Skia stays useful for text, printing and PNG export.

## Text

Text is laid out with KiCad's newstroke font (`Anode.Render.Fonts`), following KiCad's placement rules,
and emitted by `SceneBuilder` as ordinary stroke segments. Both backends, hit-testing and net highlighting
therefore handle text with no special path; the Skia system-font text was removed.
This raises the Jetson scene from 446k to 1.03M primitives: OpenGL median 5.2 ms, p95 10.2 ms, 31.5 MB of buffers.

Text whose font names a face (KiCad 7+) is emitted as filled polygons with holes instead, laid out by
`OutlineText` with KiCad's outline-font rules and HarfBuzz shaping over SkiaSharp glyph outlines. A board text
is drawn from the `render_cache` KiCad saved beside it while that still shows the same text at the same angle,
exactly as KiCad does, so a board looks as authored even without its faces; the editor moves that cache with the
text. A face this machine lacks is stood in for (monospaced for monospaced, serif for serif, else the system sans)
and reported in the checks.

## Follow-ups

1. Layer transparency: render semi-transparent layers into an offscreen texture and composite.
3. Speed up triangulation of huge zone fills: split fractured fills back into outline and holes, or cache
   triangles next to the file.
4. Level-of-detail and a spatial index for culling and hit-testing on either backend.
