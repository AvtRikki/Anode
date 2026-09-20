# Schematic editing

Status, 2026-09-19: **stages 0–3 done, parts of stages 4 and 5.** Written 2026-09-16.

- **Stage 0** — done: one command stack over tree nodes, `SchEdits`, `SchematicEditor`, incremental scene, undo and
  redo in the header.
- **Stage 1** — done, and past what was written here: the inspector was rebuilt from the designs with values that
  can be written (`E`), plus copy, cut, paste, duplicate, rotation both ways and mirroring both axes. A wire's
  stroke is still not editable.
- **Stage 2** — done: wire and bus with junctions and splitting, labels of three kinds, no-connect, junction, bus
  entry, text, line, rectangle, circle. No arc tool, and runs are orthogonal rather than 45°.
- **Stage 3** — done: `.kicad_sym`, both library tables, the index with search and a cache, the components panel
  with previews, placing a part that carries its definition with it, choosing which section of a multi-section part
  is placed, taking a definition from its library again, and swapping a placement for another part. The alternate
  body style is left out on purpose: `body_style` is 1 in all 105 placements across the demo designs and no De
  Morgan variant appears anywhere, so there is nothing to check an implementation against. The two inspector
  actions are thin wrappers over tested core writes; the buttons themselves have no test of their own.
- **Stage 4** — started: the hierarchy is walked from the project's root with KiCad's sheet paths, and a sheet
  placed twice is one tab showing one place at a time, chosen in the project tree. Designators and sections are read
  and written per place — the Reference property is only a cache, wrong for most parts on a reused sheet. Annotation
  leaves a part that already carries a number alone. Not done: numbering unique across the whole design rather than
  per sheet, a part placed on a reused sheet getting an entry for its other places, creating sheets and their pins,
  and field editing across the sheet.
- **Stage 5** — started: nets built from wires, junctions, labels of every kind, buses, power symbols and
  no-connects; the net a selection sits on, shown in the inspector; and two checks — a pin left alone on its net, and
  a designator used twice anywhere in the design, compared per place in the hierarchy. The pin-type matrix, missing
  power flags and sheet-pin checks are not done, nor the nets panel or netlist and BOM export. A net can be lit
  (backquote, or the inspector's footer): its wires, labels and dots stand out and the rest of the sheet dims.
- **Stage 6** — not started.

With nothing selected the inspector describes the document itself: a sheet its paper, title block, contents, nets
and checks; a board its size, layers, placement and routing. The sheet's title, revision, date and company can be
written there, and the frame around the drawing is KiCad's default drawing sheet — double border, 50 mm scale,
title block — redrawn when the title block changes. The comment lines are written there too: the four the default
sheet prints always, the rest of KiCad's nine when a file uses them. A board's title block is written from its own
overview the same way, and the board is drawn on its page with the same drawing sheet. A project's own drawing
sheet (.kicad_wks, named per editor in the .kicad_pro) is drawn in place of the default, with the project's text
variables; a missing one falls back to the default and says so in the checks. Pictures in a drawing sheet are drawn
by both renderers, and a text keeps its own colour and its own face. Not yet: pictures other than PNG, and editing
a drawing sheet.

Texts on the sheet — free text, labels, fields, pin names and numbers, sheet pins — and on the board are drawn in
the face their font names, laid out by KiCad's outline-font rules and shaped with HarfBuzz; bold and italic reach
the stroke font too. Checked against the letters KiCad saved in the demo boards (147 texts): the anchored edge
agrees within a few hundredths of a millimetre, top and bottom within about 0.1 mm even with a stand-in face. A face this machine lacks is
stood in for as fontconfig would and reported in the checks; a board text is drawn from KiCad's saved letters
(`render_cache`) while they still fit, so the board looks as authored regardless. A font the file itself carries
(KiCad 9's `embedded_files`, anywhere in the board or sheet) comes first, as fontconfig takes it for KiCad; the
decoding is checked against every file KiCad embedded in the demos, each matching the checksum KiCad wrote. No
demo embeds a font, so that path is tested with Noto Sans from KiCad's QA resources. Saving embeds as KiCad does:
with `(embedded_fonts yes)` — a switch in the overview's Fonts block, which also says where each face comes from —
the file of every face the document's own texts use is added when its licence (OS/2 `fsType`) allows, one already
carried under that name is kept, and with `no` carried fonts are dropped. A board looks at its own texts, not its
footprints'; a schematic keeps the setting and the files in its root sheet and looks at every sheet's texts and
labels, and a sheet below the root draws with the root's fonts. A face only stood in for is not carried, where
KiCad would carry the stand-in. Knockout text — a layer marked `knockout` — is drawn as KiCad draws it: the box around the
letters, grown by KiCad's margin, with the letters cut out of it, counters left as islands. A sheet's texts and labels are set from the inspector's Type block: the face,
from the stroke font and everything this machine or the file has, and which of KiCad's four styles. Not yet: the
same on a board, whose inspector is still read-only, and a drawing sheet embedded in the board.

Known gaps outside the stages: `G` (drag keeping wires attached), breaking a wire, cleaning up collinear wires,
aligning to grid, and a menu bar for Windows and Linux.

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
