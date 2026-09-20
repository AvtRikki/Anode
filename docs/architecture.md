# Architecture

## The shape of it

A file becomes a picture in four steps, and each step is a project that knows nothing about the one after it.

```
.kicad_pcb / .kicad_sch
        │  Anode.Sexpr        text ──► CST (every token, every space kept)
        ▼
    Anode.Kicad               typed views over the CST: Board, Schematic, Footprint, SymbolInstance …
        │                     writes go back into the same nodes; the undo stack snapshots them
        ▼
    Anode.Render              a scene: per-layer primitives (lines, circles, polygons, images) in millimetres
        │                     owners tie every primitive back to the item it came from
        ▼
 Anode.Render.OpenGl / .Skia  the same scene, drawn twice over
```

Nothing above `Anode.Kicad` knows about s-expressions, and nothing below it knows about KiCad.

## The CST is the source of truth

`Anode.Sexpr` parses into `SList`/`SAtom` nodes that keep the original text of every atom and the whitespace between
them. Writing an untouched document gives back the original bytes; a node that was changed is laid out the way
KiCad's own formatter would lay it out (`KicadPrettifier` is a port of `KICAD_FORMAT::Prettify`, kept close to the
original control flow so the output matches byte for byte).

The typed model does not copy anything: `Board`, `Schematic`, `Footprint`, `SymbolInstance` and the rest read their
values from the tree each time they are asked. An unknown token nobody has a type for still lives in the tree and is
written back untouched, which is why a board from a newer KiCad survives a round trip.

Units are integer nanometres (`Vector2L`), as in KiCad; angles are degrees in `double`.

## Editing and undo

`Anode.Kicad/Editing` holds the commands and the stack:

- `ModifyNodesCommand` snapshots the subtrees of the items it names, runs a mutation, and restores the snapshots on
  undo. It is the command almost every edit uses.
- `DeleteNodesCommand` detaches items and remembers where they were, so undo puts them back in place, not at the end.
- `RootChildCommand` is for what is not an item at all — a title block, the fonts-embedded flag — where the step has
  to remember a whole child of the root, which may not have existed before.
- `CompositeCommand` makes several of those into one step of the history.

`UndoStack` is shared by the board and the sheet, and tracks which state was last saved. `Anode.Editing` holds the
editors the canvases drive (`BoardEditor`, `SchematicEditor`): selection, move with a preview, rotate, mirror,
delete, the schematic's tools, and saving. An editor removes the items it is about to change from the scene and adds
them back afterwards, so a change redraws exactly what changed.

The invariant every edit keeps: **an edit that is undone restores the file byte for byte.** Tests hold it
(`EditingTests`, `SchEditingTests`, and the round-trip tests over every fixture).

## Scenes

`Anode.Render` turns a document into a `BoardScene` or a `SchematicScene`: layers of primitives in millimetres, each
primitive carrying an owner id. Owners are what selection, hit testing, dimming and net highlighting work in — the
renderers never hear about a footprint or a net, only about owners.

Text is not a primitive. It is laid out (`Fonts/`) into strokes of the KiCad stroke font, or into filled glyph
outlines when it names a typeface, and lands on the layer as ordinary lines and polygons. Drawing sheets — the frame
and title block — are interpreted the same way onto a layer of their own.

Both renderers draw the same scene; `SceneTriangulator` fills in triangles up front for the GPU. The choice between
them is a startup switch, and the picture is meant to be the same either way.

## The workbench and its plugins

`Anode.Sdk` is the contract, and nothing but the contract: `IPlugin`, `IPluginContext`, document types, panels,
commands, inspector rows and blocks, issues, translations, theme keys and the UI-kit helpers. `PlatformContract.Version`
is the number a plugin declares in its `plugin.json`; the loader refuses a plugin that declares another, so an SDK
change that plugins must be rebuilt for means bumping that number and the manifests with it.

A plugin registers, from `Activate(IPluginContext)`:

- **document types** — an extension, how to open a file and how to create a new one;
- **a place to write** — `IWorkbench.AskWhereToWriteAsync` asks the person where an export should go; the plugin
  writes the file itself;
- **panels** — a control, a dock area, an icon and which document types it belongs to;
- **commands** — an id, a title key, a shortcut, and what it does; the palette, menus and shortcuts all read them;
- **project structure** — how a file of this domain appears in the project tree;
- **translations** — its own catalog.

A document (`IDocument`) answers the workbench with its view, its title, whether it has unsaved changes, its status
fields, the inspector's description of the selection or of the document itself, and its checks. Everything the
workbench shows about a domain comes from there.

Plugins load into their own `AssemblyLoadContext`. The host's own assemblies — the SDK, Avalonia, SkiaSharp —
resolve from the default context so that types are shared; everything else comes from the plugin's folder.

## Documents, paths and the hierarchy

The workbench owns the paths documents live at, because two tabs writing one file is the one mistake no undo stack
can repair:

- **Opening** the same file twice shares one loading operation and one tab; a request that arrives while the first
  is still loading waits for it rather than starting a second.
- **Saving as** refuses a path another tab owns, or one a load or a save has reserved; a document may always be
  saved to its own path. Reservations are normalised absolute paths within this process — not a file lock, and not
  a resolver of every link alias.
- **Creating** a project or a sheet writes into a staging directory of its own and publishes with moves that never
  replace an existing file; a failure midway removes only what it had just published.

The hierarchy walk (`SchHierarchy.Walk`) guards itself as well: it tracks the files in the chain of ancestors it is
inside, so a sheet that places itself, or a cycle through several files, stops at that branch rather than growing;
a sheet placed twice in different branches is still two places, which is what a reused sheet is. Depth stops at 32
and the whole walk at 10,000 places, and reaching either, or meeting a file that will not read, is reported to the
document, which shows it in the checks. A partial hierarchy means design-wide answers are partial too.

## Where the modules end

- `Anode.Geometry` knows nothing of files: vectors, boxes, transforms, arcs, triangulation (`Earcut`) and region
  booleans over Clipper2 (`Clipping`), all in nanometres.
- `Anode.Kicad` knows files and the model, and nothing about drawing or UI.
- `Anode.Render` knows the model and how to draw it, and nothing about Avalonia.
- `Anode.Render.Avalonia` is the surface; the renderers are the two backends behind it.
- `Anode.Workbench` knows documents, docks and commands, and never what a net or a footprint is.
