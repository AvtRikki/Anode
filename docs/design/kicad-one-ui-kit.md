# Kicad·One UI kit (draft v0.1)

Transcribed from `Design.pdf` ("Из чего собраны макеты Kicad·One"). The kit inventories the mockups of design
rounds 1 and 2 in three layers: colours, a size scale, and components. The key split is between **drawing inks**
(copper, mask, silkscreen) and **interface paints** (chrome, states). Inks never change between the light and dark
themes; chrome changes completely.

## 1. Colours

### 1.1 Drawing inks (not themed)

| Token | Value | Use |
|---|---|---|
| `ink.copper.front` | `#d6006c` | F.Cu |
| `ink.copper.back` | `#0088b0` | B.Cu |
| `ink.mask` | `#edbb00` | Mask |
| `ink.silk` | `#201e1d` | Silkscreen, outlines |
| `ink.sheet` | `#f8f4f4` | Board body |
| `ink.inner` | 14% (grey) | In1/In2, dimmed layers |

Exactly six inks. The 5th and 6th copper layers reuse the two copper colours at 45% opacity. No further inks are
introduced, because registration stops being readable.

### 1.2 Chrome, light theme

| Token | Value | Use |
|---|---|---|
| `chrome.bg` | `#f3f2f2` | Panels, docks |
| `chrome.raised` | surface | Title bar, status bar, fields |
| `chrome.canvas` | neutral-100 | Canvas backdrop |
| `chrome.text` | `#201e1d` | Primary text |
| `chrome.text.dim` | 58% | Inactive tabs |
| `chrome.line` | 8–10% | Dock borders |

### 1.3 Chrome, dark theme

| Token | Value | Use |
|---|---|---|
| `chrome.bg` | `#1b1a19` | Panels, docks |
| `chrome.raised` | `#232120` | Title bar, status bar |
| `chrome.active` | `#2c2928` | Active tab |
| `chrome.text` | `#f3f2f2` | Primary text |
| `chrome.text.dim` | `#a8a3a0` | Minimum contrast for 11 px |
| `chrome.line` | `rgba(243,242,242,.14)` | Dock borders |

### 1.4 States: one meaning, two values

| Role | Light | Dark | Where |
|---|---|---|---|
| `accent` (interactive) | `#0088b0` | `#34b3d8` | Selection, active tab, focus |
| `alert` (needs a decision) | `#d6006c` | `#ff4d99` | DRC/ERC, out of sync, BOM risk |
| `accent.wash` | accent-100 | `#0f4d63` | Selected row background |
| `alert.wash` | accent-2-100 | `#5c0b34` | Error plate on the canvas |
| Focus ring | 2 px accent, offset 2 | same | Keyboard focus, both themes |

Rule: magenta in the interface means "your decision is needed" and nothing else. Cyan means "clickable" and
"selected". Neither may be used decoratively, or DRC markers stop standing out.

## 2. Sizes

All strip heights are fixed and independent of the theme.

- Spacing scale: 4 / 6 / 8 / 10 / 12 / 14 / 18 / 22 / 26.
- Radius 2 px, only on tabs and fields; panels and plates have no radius.

### 2.1 Window frame

| Element | Size | Rule |
|---|---|---|
| Title bar | 44 | 40 in dense mode |
| Document tab strip | 32–34 | One per split pane |
| Toolbar | 36 | Only above the canvas |
| Status bar | 26–28 | Always exactly one |
| Icon rail | 38 | Buttons 26×26 |
| Left dock | 212 (170–280) | Resizable |
| Right dock | 268 (240–360) | Resizable |
| Bottom dock | 142–150 | One height for all tabs |
| Paint strip | 120 / 44 | With list / swatches only |
| Canvas | ≥ 50% | Hard minimum of window width |

### 2.2 Typography

| Role | Size / weight | Face |
|---|---|---|
| Panel heading | 15–17 / 600 | heading |
| Large figure | 18–20 / 600 | heading |
| Body text | 12.5–13 | body |
| Tab | 12.5 | body; active 600 |
| Section overline | 10 / .11em / caps | body, accent |
| Data, coordinates, MPN | 11–12 | mono |
| Drawing label | 10–11 | mono |

Anything an engineer compares by eye down a column (coordinates, values, part numbers, times) is monospaced.
Anything read as a phrase is serif. There is no third typeface.

### 2.3 Hit targets

| Target | Size |
|---|---|
| Rail / tool button | 26×26, gap 3 |
| List row | Height 22–26 |
| Table row | 28 |
| Dock splitter (drag) | 1 visible / 6 active |

## 3. Components

Twenty components cover all nine mockups. They are grouped into frame, panel contents and canvas overlay. Each has one
meaning and one place where it lives.

### 3.1 Frame

- **TitleBar** (h44). Slots: brand ("Kicad·One"), project switcher with revision ("Nixie Clock rev D ▾"), menu or
  empty, review status, avatars. The menu appears only in variant 1a.
- **TabStrip + DocTab** (h32). Tab states: active, normal, unsaved (alert dot), dragged. The strip handles overflow
  with "» N more". Example tabs: `Power.sch •`, `Nixie.pcb`, `BOM`, and a trailing `+`.
- **IconRail** (w38). Stores panels that did not fit into docks (e.g. "Пр", "Сл", "3D", `+`). Single click shows the
  panel as an overlay; double click pins it as a dock.
- **StatusBar** (h26). Monospaced only. Left to right: coordinates, grid, selection, checks, git branch
  (`X 34.20 Y 41.66 · сетка 0.10 · DRC 1 · git: feature/usb-c`). Nothing is clickable except the branch.
- **Dock + PanelStack**. Stack = tab header + body + collapse. Stacks are separated by a line, not a frame.
  At most 2 stacks on the left, 3 on the right, 1 at the bottom. Example tabs: "Инспектор | Цепь", "Правила | Расчёты".
- **ToolBar** (h36). Every tool shows its shortcut. Icons are Phosphor duotone, 14 px in a 28×24 target.
  Example: select, rectangle, `+`, "Провод W", zoom "140%".

### 3.2 Panel contents

- **TreeRow**. Indent 16 per level. Selected row uses `accent.wash`, without a border
  (e.g. `Root.sch` › `Main.sch`, `Power.sch`, `Display.sch`).
- **LayerRow**. 12×12 swatch, drawing inks only. Empty frame means the layer is hidden. Shortcut digit on the right
  (`F.Cu 1`, `In1.Cu 2`, `B.Cu 4`).
- **PropertyGrid**. Serif label on the left with fixed width 70 or 78; monospaced value in a field
  (`Значение MCP1700-3302E`, `Позиция 21.40 / 33.02`).
- **DataTable**. Based on the system `.table`. Numbers right-aligned and monospaced; status as a tag in the last
  column (`Компонент | 100 шт | Склад`).
- **IssueRow**. Square marker, not a circle: error = alert, warning = neutral-500. Always has a coordinate and a
  "Show" action (`Зазор меньше правила · HV_170V ↔ GND · 0.18 при 0.40 · 34.2, 41.7`).
- **CommentThread**. Anchored to a net or component, not to a coordinate; the caption always names the anchor
  (`MK · VBUS заведён под кварц. Уведи по In1. · VBUS · 2 ч назад`).
- **MetricCell**. Monospaced label, serif value. Only in BOM and the revision summary
  (`Компоненты $63.15`, `Риск 2 поз.` in alert).
- **SearchField**. Same component in the title bar, the start screen and libraries
  (`Команда, компонент, цепь… ⌘K`).

### 3.3 Canvas overlay

- **CanvasCard**. Card next to an object instead of the property panel (variant 1c). Large shadow, no border,
  with a leader line to the object (`Дорожка HV_170V · 0.80 мм · 38.2 мм · ΔT 4 °C`, actions "Развести заново",
  "Класс").
- **CanvasAlert**. Error plate on the canvas next to the violation; the same error appears in the bottom dock as an
  IssueRow (`Зазор 0.18 мм при правиле 0.40 · Исправить`).
- **CommandPalette**. Width 560, anchored 76 from the top. Matches are highlighted by colour, not background;
  the command's scope is shown on the right (`прокл` → "Прокладывать дорожку X", "Автопрокладка бета").
- **SyncBanner**. The only pop-up notification in the system. Stays until the cause is resolved; never times out
  (`U3 изменён — плата не синхронизирована · Обновить ⌘⏎`).
- **SlideOverPanel**. Variant 2d. Covers the canvas edge without resizing the canvas; Esc closes it.
- **Tag · Avatar · Button**. Taken unchanged from Broadsheet. Tag accent = all good, accent-2 = decision needed,
  neutral = fact, outline = action on the row (`склад`, `снимается`, `архив`, `Показать`, primary `Заказать`).

### 3.4 ECAD-specific

- **LayerStack**. Layers as plates; plate height = visibility. Replaces the checkbox list of variant 1d.
- **RevisionRail**. Circle = revision, square = released order. The current revision is larger and in alert
  (`rev B · заказ · rev D`).
- **CanvasSheet**. The canvas is light in both themes: 14 px dot grid, board body in `ink.sheet`, registration marks
  in the corners, drawing inks.

## 4. Open questions (from the kit)

- **Icons.** Mockups use primitives. Phosphor duotone covers the interface but not editor tools (track, via,
  polygon, bus); those are drawn separately at 16 and 24.
- **Dense mode.** Broadsheet assumes 1.25× density; ECAD on a 13″ screen cannot afford it. A second height set
  (−4 per strip) behind one switch, without changing typography.
- **Multilayer boards.** Six inks cover 4 layers. 8–12 layers need another mechanism: the active layer in full
  colour, the rest in shades of grey.
- **Dark canvas.** The canvas is always light. A dark canvas, if engineers ask for it, is a separate ink set, not
  an inversion; decide after user testing.

## Implementation notes (0.1)

What the workbench does today, and where it knowingly differs from the kit.

| Kit | Implementation |
|---|---|
| Chrome tokens, both themes | `src/app/Anode.Workbench/Themes/Anode.axaml`, keys in `src/app/Anode.Sdk/ThemeKeys.cs`; dark is the default |
| Drawing inks | `LayerStyle.Inks = InkSet.Print` (`src/render/Anode.Render/LayerStyle.cs`): six inks, inner copper one grey |
| Light canvas | The desk is `#e9e6e5` in both themes; copper is drawn at 85 %/77 % so a pour does not bury silk |
| Dot grid | Dots at the grid crossings in both renderers; the spacing follows the zoom (mm), not a fixed 14 px |
| Board body in `ink.sheet` | Filled from the `Edge.Cuts` loops (`OutlineLoops`), under every layer; inner cut-outs are not punched out yet |
| Phosphor duotone icons | Own pack instead: `Anode.Sdk.Icons` draws line art on a 16×16 grid, a 1.3 px stroke over a 20 % fill — two tones, no assets, no third-party licence. Plugins add their own with `Icons.Register` |
| No menu bar | Matches the mockups: every action is in the command palette; the project chip holds open / start page / language |
| Languages | English by default, Russian included; catalogs per assembly, switched from the palette (`Tr`, `i18n/*.json`) |
| Panels by domain | A panel declares the document types it belongs to (`PanelDescriptor.DocumentTypes`); the workbench re-plans the docks when the active tab changes, so layers show with a board and the sheet hierarchy with a schematic |
| Two domains | PCB and schematic are separate plugins; the schematic sheet is drawn with the same inks — dark strokes on the sheet, cyan for labels and buses |
| Own title bar | The client area is extended into the decorations, so the kit's 44 px bar *is* the title bar. macOS keeps its window buttons in a band of its own (28 pt) whatever height the window claims, so the title bar view is grown to 44 and the buttons are centred in it through the Objective-C runtime (`MacTitleBar`), the way VS Code and the JetBrains IDEs do it; re-applied on resize and full screen |
| Project switcher | A label until the pointer arrives, then a frame and a chevron (Rider's implicit combobox). Hidden entirely while no project is open; the version control branch sits next to it, read from `.git` |
| Four dock places, rail overflow | `DockPlanner` (2 left stacks, 3 right, one bottom stack), rail slide-over with pin on double click |
| Command palette | 560 wide, 76 from the top, fuzzy ranking in `CommandMatcher` |
| SyncBanner | One banner at a time, shown over the canvas, dismissed by hand |

Headless snapshots of these screens are produced by `tests/app/Anode.Workbench.Tests` (`snapshots/*.png`).
