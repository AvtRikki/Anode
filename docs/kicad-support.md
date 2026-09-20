# What of KiCad is supported

KiCad 8, 9 and 10 files (s-expression). Version 6 and 7 and the legacy formats are not read.

Everything here is measured against KiCad's own demo and QA files (`tools/fetch-fixtures.sh`) and, where the rule is
subtle, against KiCad's source. What is not done is listed as plainly as what is.

## Round trip

Reading and writing a file without editing it reproduces the original bytes, for every fixture. Unknown tokens live
in the tree untouched, so a file from a newer KiCad survives. An edit that is undone restores the file byte for byte;
a node that was changed is laid out as KiCad's formatter lays it out.

Two deliberate exceptions, both KiCad's own behaviour on save: with `(embedded_fonts yes)` the fonts a document's
texts use are added to it, and with `no` any it carries are dropped.

## Boards (`.kicad_pcb`)

Read and drawn: layers and the stackup's names, nets, footprints with pads of every shape (circle, rect, oval,
roundrect, chamfered, trapezoid, custom) and their drills, tracks, arcs, vias including blind and buried, zones with
their filled polygons, board graphics, dimensions, groups, text and text boxes, and the board's own embedded files.

Edited: footprints, tracks, arcs, vias, graphics, zones and texts — select, move on a 0.1 mm grid, rotate, delete,
undo/redo, save. Of the inspector's rows, only a text's face and style are written; everything else it shows is
computed.

Not done: routing, zone filling (a zone is drawn from the fill the file carries), DRC, 3D, the stackup dialog,
editing pads or footprints.

## Schematics (`.kicad_sch`)

Read and drawn: symbols with their libraries' definitions, units and body styles, wires and buses, junctions,
no-connects, bus entries, labels of every kind (local, global, hierarchical, net-class flags), text and text boxes,
graphics, sheets and their pins, the title block, and instance data — the designator and unit a part carries in each
place of the hierarchy.

Edited: everything above can be moved, rotated, mirrored and deleted; wires, buses, labels, junctions, no-connects,
bus entries, text, lines, rectangles and circles are drawn with tools; parts are placed from `.kicad_sym` libraries
(both library tables, an index with search and previews), a placement can be updated from its library or swapped for
another part, and a sheet's fields and title block are written from the inspector. Annotation numbers what is
unnumbered, per place.

**Hierarchy.** A design is walked from the project's root; a sheet placed twice is one tab showing one place at a
time, chosen in the project tree, and designators and units are read and written per place.

**Nets.** Built from wires, junctions, labels of every kind, buses (vector and group, with `bus_alias`), power
symbols, no-connects and the pins of child sheets. A label or a pin landing part way along a wire is on that wire;
a junction dot is required only where two wires meet, as in KiCad. Across a design, sheets are joined through a
sheet symbol's pins and the hierarchical labels inside the sheet, and through global labels and power symbols; nets
are named as KiCad names them.

**Checks.** A pin left alone on its net, a designator used twice anywhere in the design (compared per place), the
format version, a symbol whose definition is missing, a drawing sheet that will not read, a face this machine lacks,
and what went wrong while the hierarchy was walked — a child sheet that will not read, a cycle, or a traversal limit.
A child sheet that fails to parse leaves the rest of the design open rather than stopping the load; opening that file
itself still fails, as it must.

Not done: the pin-type matrix, missing power flags, sheet-pin mismatches, netlist and BOM export, creating sheets
and their pins, numbering unique across a whole design, and the alternate (De Morgan) body style.

## Drawing sheets (`.kicad_wks`)

KiCad's default sheet is drawn when a project names none, and is our own written copy of it — proven equal to the
interpreted one on four paper sizes before it was kept. A project's own sheet is read from the path in the
`.kicad_pro` (per editor, with `${KIPRJMOD}`, text variables and environment variables resolved) or from the
document's own embedded files when the project names it `kicad-embed://name.kicad_wks`.

Interpreted as KiCad interprets it: corners, repeats with their increments, both ends checked against the page,
`page1only`/`notonpage1`, the legacy `%C0`-style codes, text variables and title-block fields, lines, rectangles,
polygons (rotated and filled), PNG pictures, and a text's own colour and typeface.

Not done: pictures other than PNG, and editing a drawing sheet.

## Text

Laid out by KiCad's rules, ported from its source: the stroke font (newstroke) with its glyph scale, baselines, line
pitch, italic tilt, markup (`~{overbar}`, `^{superscript}`, `_{subscript}`) and pen widths; and outline fonts with
the 1.4 em compensation, first baseline one height below the top, 1.68 line pitch, and HarfBuzz shaping so kerning
and ligatures match. Escapes are undone before a text is shown or named: `VPP{slash}MCLR` reads and connects as
`VPP/MCLR`.

A face the machine lacks is stood in for (monospaced for monospaced, serif for serif, else the system sans) and
reported in the checks. A board text is drawn from the `render_cache` KiCad saved beside it while that still shows
the same text at the same angle, so a board looks as its author saw it; moving or turning the text in the editor
carries that cache along. Knockout text is a box with the letters cut out of it, with KiCad's margin.

Fonts a document carries (`embedded_files` with `(type font)`) are decoded — zstd, base64, KiCad's MurmurHash3
checksum with its pre-fix variant and SHA-256 for older files — and used ahead of installed faces. Saving with
`(embedded_fonts yes)` adds the file of each face the document's own texts use when its OS/2 licence allows.

Not done: fonts for the board's inspector beyond face and style, and a face chooser for anything but texts.

## Projects and libraries

`.kicad_pro` is read for text variables and the drawing sheets; `sym-lib-table` (global and project) for symbol
libraries, with an index, search and a cache. `fp-lib-table` and footprint libraries are not read yet.

## Embedded files

`(embedded_files …)` is read wherever it appears — board, sheet, footprint or symbol: zstd inside base64 between
bars, with KiCad's checksum. Fonts and drawing sheets are used; models, datasheets and other files are kept but not
opened. Writing back is exact enough that a file removed from a board and put back again reproduces the original
bytes.
