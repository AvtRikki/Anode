# Where files go

Written 2026-09-17, when git support was being considered for projects.

A project folder belongs to the person using it. Everything in it is theirs to commit, review and carry between
machines, so nothing this application keeps for its own convenience may be written there.

## The rule

- **The project folder holds design files only** — what KiCad itself reads and writes, and what belongs in a commit.
- **Everything else goes to the application's own folder**, `AppPaths.DataDirectory`: `%AppData%\Anode` on Windows,
  `~/Library/Application Support/Anode` on macOS, `~/.config/Anode` elsewhere.
- **A cache is not a file unless it has to be.** The library cache holds parsed libraries in memory and writes
  nothing; a cache only earns a file when rebuilding it is genuinely expensive across sessions, and then it goes to
  the application's folder, never beside the design.
- Plugins reach that folder through `IPluginContext.DataDirectory` rather than working it out themselves, which is
  also what lets the tests point it somewhere of their own.

## What is written where today

Into the project folder, and all of it design:

| File | When | Why it belongs in a commit |
| --- | --- | --- |
| `<name>.kicad_pro` | creating a project | the project |
| `*.kicad_sch`, `*.kicad_pcb` | saving | the design |
| `sym-lib-table` | "Add library…" | KiCad's own record of which libraries the project uses |

Into the application's folder, and none of it design:

| File | What it remembers |
| --- | --- |
| `recent.json` | recently opened projects |
| `settings.json` | the interface language |
| `symbol-libraries.json` | libraries this application was told to remember, offered in every project |
| `symbol-sources.json` | which library sources a given project has been told not to show |

Held only in memory:

- parsed symbol libraries (`SymbolLibraryCache`), keyed by path and dropped when the file's write time or length
  changes.

## Why the split matters

A cache written beside a design turns into a question at every commit: is this mine, is it noise, does it differ
between machines? The answer is always "noise", and answering it repeatedly costs more than putting the file
somewhere it never has to be answered for.
