# Schematic editing

Status, 2026-09-18: **stages 0–2 done, stage 3 half done.** Written 2026-09-16.

- **Stage 0** — done: one command stack over tree nodes, `SchEdits`, `SchematicEditor`, incremental scene, undo and
  redo in the header.
- **Stage 1** — done, and past what was written here: the inspector was rebuilt from the designs with values that
  can be written (`E`), plus copy, cut, paste, duplicate, rotation both ways and mirroring both axes. A wire's
  stroke is still not editable.
- **Stage 2** — done: wire and bus with junctions and splitting, labels of three kinds, no-connect, junction, bus
  entry, text, line, rectangle, circle. No arc tool, and runs are orthogonal rather than 45°.
- **Stage 3** — the reading half is done: `.kicad_sym`, both library tables, the index with search and a cache, the
  components panel with previews, and placing a part that carries its definition with it. Still missing: choosing a
  unit of a multi-unit part, the alternate body style, changing a symbol, and "update from library".
- **Stages 4–6** — not started.

Known gaps outside the stages: dragging a part from the panel onto the sheet, `G` (drag keeping wires attached),
breaking a wire, cleaning up collinear wires, aligning to grid, and a menu bar for Windows and Linux.

The board can be edited; the schematic can only be looked at. This is the plan that closes that gap, in the order
the work actually has to happen, with the reason each stage exists.

## Where we are

`BoardEditor` (in `Anode.Editing`) already does selection by click and by box, a move with a live preview, rotation,
deletion, undo/redo, saving and grid snapping. Underneath it, `Anode.Kicad/Editing` holds the undo stack and two
commands: `ModifyItemsCommand`, which snapshots the items' CST subtrees and restores them on undo, and
`DeleteItemsCommand`, which detaches top-level items and remembers their index in the file so undo puts them back
exactly where they were. That snapshot-and-restore is what makes an edited-then-undone file byte-identical again, and
`EditingTests` holds that invariant.

The schematic side has none of it. `SchematicCanvas` pans, zooms and picks **one** item; `SchematicDocument.CanSave`
is `false`; its checks cover the format version and symbols whose definition is missing, nothing more.

Three gaps are worth naming before they surprise us:

- **Instance data is not modelled.** `(instances (project … (path … (reference "U2") (unit 1))))` passes through as an
  unknown node. There are 106 of them in `pic_programmer.kicad_sch` alone. Annotation cannot exist until this is typed.
- **There are no symbol libraries.** Nothing reads `.kicad_sym` or `sym-lib-table`, so there is nothing to place a new
  component *from*.
- **The schematic scene is build-once.** `BoardScene.Remove` supports incremental edits; `SchematicScene` has no
  equivalent, so today every change would mean rebuilding the whole sheet.

## Invariants

1. **Lossless round-trip survives editing.** An untouched node is written back as its original text; an edit that is
   undone restores the file byte for byte. Every stage carries a test of this, following `EditingTests`.
2. **One undo stack, one notion of a command.** The board and the schematic share the machinery; only the item types
   differ.
3. **The domains stay apart.** The shell knows nothing about `.kicad_sch`; the schematic plugin contributes editing,
   structure and checks through the SDK.

## Stage 0 — the foundation

Generalise what was written for the board, and give the schematic the same footing.

- Lift the command stack from `BoardItem` to tree nodes: `IEditCommand`, `ModifyNodesCommand`, `DeleteNodesCommand`
  (detach with the index, attach back), one `UndoStack` that also tracks the saved state. The board becomes one user
  of it rather than its owner.
- `SchEdits`, the twin of `BoardEdits`: what can be moved, where its anchor is, and how each kind transforms — a
  symbol by `at` + angle + `mirror`, a wire by its `pts`, a label by `at` + angle, a sheet by `at` + `size` and its
  pins, graphics by their points.
- `SchematicEditor`: selection (click, box, add to selection), move with preview, rotate, mirror, delete, undo/redo,
  save, snapping to the schematic grid of 1.27 mm (not the board's 0.1 mm).
- `SchematicScene`: incremental removal and re-adding of an item's primitives, the counterpart of `BoardScene.Remove`.
- `SchematicCanvas`: multiple selection, a box that selects by crossing when dragged right to left, dragging the
  selection, `Esc`, `Delete`, `R`, `M`.
- `SchematicDocument`: `CanSave` true, dirty state from the history, and the document's commands registered while it
  is active.
- **Undo and redo in the title bar.** The buttons live in the window header and are driven by the active document:
  it says whether undo exists at all, whether it is available right now, and what it would undo. The header reads
  that through the command registry (`edit.undo` / `edit.redo`, `CanExecute`), so a plugin that registers those
  commands gets the buttons, and one that does not has none.

## Stage 1 — edit what is already on the sheet

Move, rotate, mirror and delete symbols, wires, labels and text. Edit a symbol's fields (reference, value, footprint)
in the inspector, and a wire's stroke. This is the point where the schematic is as editable as the board is today.

## Stage 2 — drawing, and with it the notion of a tool

There is no tool framework anywhere in the app yet — the board canvas has gestures, not tools. It is written here and
later serves board routing too: `ITool` with a preview on its own scene layer, hotkeys, and hints in the status bar.

Tools: wire and bus (orthogonal and 45°, junctions where needed, splitting a wire that is tapped into), labels of all
four kinds, no-connect, bus entry, graphics and text.

## Stage 3 — symbols and libraries

Read `.kicad_sym` with the same lexer, both `sym-lib-table` files (global and project), an index and a search over
them. Choosing a symbol with a preview; placing an instance (`lib_id`, `at`, `unit`, `uuid`, fields) and writing its
definition into the sheet's `lib_symbols`. Changing a symbol, picking a unit, the alternate body style, and
"update from library".

## Stage 4 — identity and hierarchy

Type the `(instances …)` block: project → sheet path → reference and unit. Annotation (automatic, re-annotation,
reset) that understands that one file can sit in the hierarchy twice. Creating sheets, their pins, and keeping those
pins in step with the hierarchical labels inside. Field editing across the sheet, DNP and the BOM/board exclusions.

## Stage 5 — connectivity and checks

Build nets from wire segments, junctions, labels (local, global, hierarchical), buses (vector and group, with
`bus_alias`), power symbols and no-connects. ERC on top: unconnected pins, the pin-type matrix, duplicate references,
missing power flags, sheet pins that do not match. Results go into the checks dock we already have, each with a
location and a "show" action. A nets panel (the left-top place is free now) and net highlighting on the canvas.
Netlist and BOM export, so the schematic reaches the board.

## Stage 6 — meeting the board

Symbol ↔ footprint association, "update board from schematic", back-annotation. Only after connectivity exists.

## Decisions

- **Generalise the command stack rather than duplicate it.** Two stacks would drift apart, and the round-trip
  invariant would have to be proven twice.
- **A project-wide context** (sheet tree, instances, library cache) is only needed from stage 4, but its seam is left
  in place at stage 0, because annotation and ERC would otherwise force the editor to be rewritten.
- **Undo/redo belongs to the document, the buttons belong to the frame.** The header shows what the active document
  supports; it never assumes a document can undo.
