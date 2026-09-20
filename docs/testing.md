# Tests

```bash
dotnet test --solution Anode.slnx
```

xUnit v3, one test project per source project, laid out like `src`:

| Project | What it holds |
|---|---|
| `tests/core/Anode.Sexpr.Tests` | Parsing, writing, the formatter |
| `tests/core/Anode.Geometry.Tests` | Vectors, boxes, arcs, triangulation, region booleans |
| `tests/core/Anode.Kicad.Tests` | The model, the writes, connectivity, hierarchy, drawing sheets, embedded files, round trips over every fixture |
| `tests/core/Anode.Editing.Tests` | The editors: selection, move, tools |
| `tests/render/Anode.Render.Tests` | Scenes, fonts, layers, hit testing |
| `tests/render/Anode.Render.Skia.Tests` | Offscreen Skia renders, written out as PNGs |
| `tests/render/Anode.Render.OpenGl.Tests` | Headless GL (CGL on macOS), checked by reading pixels back |
| `tests/plugins/…` | Each plugin's own: documents, inspector rows, issues |
| `tests/app/Anode.Workbench.Tests` | The workbench in a headless Avalonia window: docks, panels, commands, the inspector, opening and saving |

`tests/Shared` is linked into every test project; `TestData` finds the fixtures.

## Fixtures

```bash
./tools/fetch-fixtures.sh
```

KiCad's own demo and QA files, downloaded into `test-data/` (about 176 MB, not committed — licences vary). They are
pinned to a commit of the KiCad repository; a file added to KiCad later is fetched from the commit that added it.
Without the fixtures those tests **skip**, they do not fail, so a clean checkout is still green.

Fixtures are how a rule is settled: a question about KiCad's behaviour is answered by measuring every demo, not by
reading one. That is what caught, among others, references being per place rather than per symbol, and labels part
way along a wire being connected.

## Pictures

The Skia render tests and the headless workbench tests write PNGs — renders into `test-output/renders/`, window
snapshots next to the test binaries in `snapshots/`. `ANODE_SNAPSHOT_DIR` sends both somewhere else:

```bash
ANODE_SNAPSHOT_DIR=/tmp/shots dotnet test --solution Anode.slnx
```

They are written to be looked at. Nothing compares them to a stored image: a golden-image suite over a drawing this
young would go stale faster than it would catch anything. The assertions check what can be stated — that the letters
reached the pixels, that a counter stayed open, that a layer has polygons — and the picture is there for the eye.

## Writing a test here

- Name it as a sentence about the behaviour: `A_label_part_way_along_a_wire_names_that_wire`.
- Prefer a real fixture over a hand-made file when the question is about KiCad; prefer a hand-made file when the
  question is about one rule, so the test says what it is about.
- A test that needs a fixture skips without it: `Assert.SkipWhen(TestData.AnySchematic() is null, TestData.SkipReason)`.
- The headless workbench tests run on one UI thread through `ShellWindowTests.Dispatch`; they pump the dispatcher
  rather than sleeping.
- Tests run side by side. Anything global — the translation catalogs, the font registry — has to be safe for that;
  an assembly fixture registers a plugin's catalog once for the whole assembly.
