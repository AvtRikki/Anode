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
graphics, sheets and their pins, pictures the sheet carries (PNG, drawn about their point at the size the picture
itself gives), rule areas (drawn closed, as the boundary they are), tables, the title block, and instance data — the
designator and unit a part carries in each place of the hierarchy.

A stroke's style is read: a line written as dashed, dotted or a mixture of the two is drawn that way, with the
lengths KiCad takes from ISO 128-2 — eleven widths of dash, a fifth of a width of dot, four widths of gap. The
cutting is done in the world the drawing lives in, so a dash is a length on the sheet and looks the same at any
zoom and through either backend.

Tables are drawn as KiCad draws them: the text of every cell inside its own margins, and the lines between the
cells added cell by cell — each one draws its right and bottom edge unless it already reaches the table's edge,
which is what makes a cell spanning two columns leave out the line it spans. The first row's lines are the border's
rather than the separators', when the table asks for a header. Ellipses and elliptical arcs, which KiCad 10 added
and none of its demos use yet, are drawn too.

A group is not drawn, and KiCad does not draw one either: it is a way of holding items together, and only its
members are ever on the page. What we lack is selecting them together, which belongs with editing rather than here.

Edited: everything above can be moved, rotated, mirrored and deleted; wires, buses, labels, junctions, no-connects,
bus entries, text, lines, rectangles and circles are drawn with tools; parts are placed from `.kicad_sym` libraries
(both library tables, an index with search and previews), a placement can be updated from its library or swapped for
another part, and a sheet's fields and title block are written from the inspector. Annotation numbers what is
unnumbered, per place, and takes the first number nobody in the design has — counting every place of every sheet,
as KiCad does, so that no two parts of one design are called the same thing. A part placed from the panel is named
the same way as it lands, and a number handed out is not offered again. A sheet can also be numbered again from
scratch, which renames what already carries a number: within a prefix the parts are numbered left to right and then
top to bottom, as KiCad's own default ordering does it, the sections of one part go on sharing a designator, and
the numbers the rest of the design holds are still not offered.

**Hierarchy.** A design is walked from the project's root; a sheet placed twice is one tab showing one place at a
time, chosen in the project tree, and designators and units are read and written per place. A sheet can be made:
the rectangle that stands for it is written with the fields KiCad autoplaces and the path and page it holds in the
design, and the schematic it reads is written beside its parent in the parent's own format version and paper — or
an existing file is read, if one of that name is already there. Its pins are kept on its edge, wherever they are
put, and carry the edge as the angle KiCad writes for it; a pin made for a hierarchical label that is already
inside the sheet takes that label's shape, so the two agree.

**Nets.** Built from wires, junctions, labels of every kind, buses (vector and group, with `bus_alias`), power
symbols, no-connects and the pins of child sheets. A label or a pin landing part way along a wire is on that wire;
a junction dot is required only where two wires meet, as in KiCad. Across a design, sheets are joined through a
sheet symbol's pins and the hierarchical labels inside the sheet, and through global labels and power symbols; a
bus running into a child sheet carries its members in, each meeting the member of the same short name inside, and
an alias one sheet declares is known to them all. Nets are named as KiCad names them.

**Checks.** The electrical rules between the pins of a net, over the whole design and by KiCad's own tables: pins
that may not be wired together (two outputs, an output and a supply, anything on a no-connect pin) and a net with a
pin waiting to be driven and nothing driving it — a power input with no power output on the net is KiCad's missing
power flag — and a sheet symbol's pin that names nothing inside the sheet, or a hierarchical label the symbol above
has no pin for. A pin left alone on its net, a designator used twice anywhere in the design (compared per place), the
format version, a symbol whose definition is missing, a drawing sheet that will not read, a face this machine lacks,
and what went wrong while the hierarchy was walked — a child sheet that will not read, a cycle, or a traversal limit.
A child sheet that fails to parse leaves the rest of the design open rather than stopping the load; opening that file
itself still fails, as it must.

How strictly each rule is applied is the project's to say: `erc.rule_severities` in the `.kicad_pro` softens a rule
to a warning or silences it altogether, and `erc.pin_map` replaces the matrix of pin types with the project's own.
The pin matrix is two settings at once, as KiCad reads it — `pin_to_pin` says only whether conflicts are reported,
and the matrix cell says whether one is a warning or an error. A project that says nothing, or one that will not
read, leaves KiCad's defaults standing.

**Netlist.** The design is exported as KiCad's own `(export (version "E") …)`: the sheets of the design with their
title blocks, the parts of every place with what they were drawn from and where they stand, the definitions with
their pins, and the nets — named as KiCad names them (`/sheet/LABEL`, `Net-(R1-Pad2)`, `unconnected-(U2-NC-Pad3)`),
ordered by name, their nodes by designator and pin, power symbols never a node. Measured against the netlists KiCad
exported from its own QA schematics — a plain hierarchy, no-connects, a bus running into child sheets, and sheets
named through bus aliases — where ours says the same, net for net and pin for pin.

**Bill of materials.** The parts of the design are exported as the CSV of KiCad's own default preset ("Grouped By
Value"): the columns `Reference,Value,Datasheet,Footprint,Qty,DNP`, every field quoted, one line per part that is
the same thing — the same value, datasheet, footprint and do-not-place — with its designators in one field, in
KiCad's natural order. A part on a sheet placed twice is two parts, under the designator each place gives it. A
power symbol is not a part, nor is one the file marks `(exclude_from_bom yes)`; a part marked do-not-place is on a
line of its own, as that column keeps it apart.

A part that carries a second body — KiCad's De Morgan alternative — can be switched between its two ways of being
drawn from the inspector, and is drawn, wired and counted from the bodies of the way it is in. A body written for
unit 0 is common to every section but still belongs to one way of drawing, which is the rule KiCad reads it by.

A net where many pins quarrel is not a line for every pair, which KiCad also refuses to be: the pins are taken in
the order of how well each type speaks for a conflict — an unspecified pin says more than a power output, which is
what KiCad's weights mean — and each pin swallows every pair it takes part in and is reported once, against
whichever of those partners stands nearest to it. A partner on another sheet is taken only while nothing on this one
has been found, and pins of one part drawn on top of each other, as a chip's several ground pins are, are one
connection rather than a quarrel with themselves.

Not done: numbering a whole design again in one step (a sheet at a time is what the editor offers, since renaming
parts on sheets that are not open would edit files behind the designer's back), and the exclusions a project lists
for single findings — matching one needs the marker written exactly as KiCad writes it, down to its position.

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
