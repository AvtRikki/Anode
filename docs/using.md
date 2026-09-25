# Using Anode

```bash
dotnet run --project src/app/Anode.Workbench -- path/to/board.kicad_pcb
```

A file can also be opened with ⌘O or dropped on the window; the start page lists recent projects. `--lang=ru` sets
the language, `--renderer=skia` the backend.

## Creating and saving files

New projects and new sheets refuse to replace existing files at their final locations. If a name is already in
use, choose another name. Repeated requests to open the same file share a single loading operation and tab.

Save As cannot use the path of another open, opening or saving document. Save or close the other tab before
choosing its path; saving to the current document's own path is allowed.

When a project's hierarchy contains a cyclic reference or an unreadable child sheet, the readable part opens
and Checks reports the affected file. Hierarchy traversal also has depth and instance limits; a limit warning
means the design was loaded only partially. Reopen the document after repairing a child file externally to
refresh these loading diagnostics.

## The window

- **Left top — Project.** The project's schematics and boards, the sheets of a hierarchy under their root. A sheet
  placed twice appears twice; choosing a place opens that place in the tab.
- **Left bottom — Layers** (board) **or Nets** (sheet).
- **Right top — Inspector**, beside the **Components** panel on a sheet.
- **Bottom — Checks**, **Console**, and **Find** on a sheet.

Every panel is also an icon in the rail; a stack can be collapsed, and a panel sent to the rail and back. ⌘K opens
the command palette, which lists every command with its shortcut.

## Looking around

- Wheel: zoom around the cursor. Shift + wheel: pan horizontally.
- Middle or right drag, or Space + left drag: pan.
- Home: zoom to fit.
- The status bar shows the cursor in millimetres, what is selected, the lit net, and the renderer with its frame time.

## Boards

- Click selects; Shift+click adds or removes. Clicking a pad selects its footprint and lights the pad's net.
- Drag from empty space to box-select: left-to-right takes what is enclosed, right-to-left what is touched.
- Drag a selected item, or press M and click to place it: the anchor snaps to a 0.1 mm grid.
- R rotates 90° counter-clockwise, Shift+R clockwise, also while moving. Delete or Backspace deletes.
- Esc cancels a move or clears the selection.
- The layers panel shows copper first with a digit each; a click hides or shows a layer, and the button at the bottom
  lists every layer the board has rather than the usual few.

## Schematics

- The same selection, move, rotate and delete; M, R, Shift+R, and Y and X mirror. Pulling a selection with the
  mouse keeps its wiring, as KiCad does by default: the wires that meet it keep hold and stretch, and are drawn
  stretching as the pointer moves. G does the same from the keyboard; M is the one that tears a part away from its
  wires, for putting it somewhere else entirely. Wires keep their right angles: a corner slides along the next
  wire, and where the far end is held by something a step is put in near the pin.
- Tools: wire, bus, bus entry, junction, no-connect, labels (local, global, hierarchical), text, text box, cut,
  sheet, sheet pin, line, arc, curve, rectangle, circle. Esc returns to selecting.
- While drawing a wire, the crosshair snaps to a nearby visible symbol pin; a small cross marks the connection point.
  The route keeps its corner while the pointer moves, favors the side that crosses fewer symbols, and Tab switches
  the corner until the next click.
- An arc takes three clicks — where it starts, where it ends, then a point it passes through — and a curve four.
  Between clicks the shape follows the pointer, so what will be drawn is what is shown.
- The cut tool divides a wire where it is clicked and puts a dot there; a click on an end of a wire does nothing.
- A sheet is drawn as a rectangle in two clicks and then named; the schematic it reads is written beside this one
  under that name, unless a file of that name is already there, which it then reads instead. The sheet pin tool
  puts a pin on the edge of a sheet nearest where it is clicked — the preview shows where it would land.
- The components panel searches the symbol libraries and places a part; the pointer stays armed, so a row of them
  can be laid down. A part drawn in sections asks which one to place.
- The nets panel lists the nets of the sheet with what each reaches; a switch turns it to the whole design's nets.
  Choosing a row lights that net, choosing it again puts it out. The backquote does the same from a selection.
- **Edit → Write as a label / global label / hierarchical label / text** changes what selected words mean without
  moving them. What is already of that kind is left alone.
- A selected child sheet lists in the inspector, under **Pins and labels inside**, what does not agree with the
  hierarchical labels in its file: labels with no pin, pins with no label, and pins whose shape differs. The
  footer adds pins for the labels (outputs on the right edge, the rest on the left, 2.54 mm apart, clear of the
  pins already there), gives pins their labels' shapes, or removes the pins that name nothing; a pin with no label
  can be pointed at a label with no pin, which renames it. Each is one step to undo. Only the pins are written:
  the labels live in the child's file, and are changed there.
- **Edit → Find** (⌘F) opens the find panel at the foot of the window; **Find and replace** (⌘⌥F) puts the
  caret in the replace box. Every place on the sheet that matches is listed with what it belongs to; Enter or F3
  goes to the next, Shift+Enter or Shift+F3 to the one before, and each is selected and brought into view. Replace
  changes the place on show and goes on; Replace all changes every place on the sheet as one step to undo.
  Switches: match case; anywhere, whole words, wildcards or a regular expression; hidden fields; pin names and
  numbers; replacing designators (off, so replacing "R" in values does not rename every resistor); and the
  selection only, taken when the switch is turned on.
- **Edit → Align** brings the selection to one edge, to its middle, or onto the grid. Things only slide.
- **Edit → Hold in place / Let go** locks what is selected. A held item does not move, turn or delete with the rest.
- **View → Show hidden fields / hidden pins** brings out what the sheet keeps out of sight, and puts it back.
- A child sheet's page number is written in the inspector, for the place the sheet stands in.
- Annotation numbers the parts that carry no number yet, per place in the hierarchy. The number is the first one
  free in the whole design, not on this sheet alone, so two sheets never hand out the same designator; parts that
  already carry a number are left alone.
- **Edit → Renumber the sheet** is the other half of that: it numbers this sheet's parts again from scratch, left to
  right, renaming what already carries a number. One undo takes it back, and no other sheet is touched.
- **File → Export netlist…** writes the whole design's netlist in KiCad's format, wherever the picker points. It is
  the design's, not the sheet's: exporting from a sheet below the root still describes the project, with this sheet
  as it stands in the editor.
- **File → Export BOM…** writes the design's bill of materials as the CSV KiCad's default preset writes: one line
  per part that is the same thing, with its designators, quantity and whether it is to be placed.

## The inspector

With something selected, the inspector describes it in blocks — what it is, what it connects to, how it is set, where
it stands. A value on the field fill can be written; a bare one was computed. E puts the caret in the first value
that can be written. A part drawn in sections is asked which one it is; a part that carries a second body is asked
which way it is drawn — the De Morgan alternative is a switch there. Switches also say what the part is left out of:
do not populate, keep off the bill, keep off the board, keep out of simulation.

With nothing selected it describes the document itself: a sheet its paper, title block, contents, nets and checks; a
board its size, layers, what is placed and routed. The title block is written from there, and so is which fonts the
file carries.

## Checks

The bottom dock lists what the checks found, each with a place and a button that shows it — including the
electrical rules: pins that may not be wired together, and nets nothing drives. Those are worked out across the
whole design, and each is shown on the sheet the pin stands on, where the button turns the tab to that place,
selects the part and brings it into view — centred, and moved closer only when it would otherwise be too small to
find, since the zoom you set is yours. What the project's own settings say about a rule is followed: one softened
to a warning is shown as one, and one the project silences is not shown at all. They are recomputed as the document
changes; a project's missing drawing sheet or a font this machine lacks is reported there too.

## Files

⌘S saves, ⌘⇧S saves as. Undoing every edit gives the file back byte for byte, so saving an untouched file changes
nothing. Closing a document with unsaved changes, or opening another one, asks first.
