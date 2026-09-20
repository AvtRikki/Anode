# Anode documentation

Four kinds of document live here, and they are kept apart on purpose.

| Where | What it holds | When it changes |
|---|---|---|
| [architecture.md](architecture.md) | How the program is put together: the modules, what flows between them, editing and undo, how a plugin is loaded and what it may contribute | A module, a seam between modules, or the plugin contract changes |
| [kicad-support.md](kicad-support.md) | What of the KiCad formats is read, written and drawn, how faithfully, and what is not done yet | Anything about reading, writing or drawing a KiCad file changes |
| [using.md](using.md) | What the program does for the person using it: panels, the inspector, shortcuts, checks, languages | Anything visible on screen changes |
| [testing.md](testing.md) | How the tests are laid out, the fixtures they need, the snapshots they write | The way the suite is run or arranged changes |
| [design/](design/) | Plans written before the work: the stages of a feature, what is done and what is left | While a plan is being carried out |
| [adr/](adr/) | Decisions taken and why, with the measurements behind them | A decision is taken, or reversed |
| [review/](review/) | A review of the code at a moment, its findings and what was done about them | A review is carried out |

Two rules keep these honest:

- **A change and its documentation belong in one commit.** A feature that is not written down here did not happen,
  and a line here that the code no longer does is worse than no line at all.
- **Say what is not done.** Every document names its own gaps. A gap that is written down is a decision; one that is
  not is a bug waiting to be found by somebody who trusted the page.

`README.md` at the root answers "what is this, how do I build and run it". Everything longer belongs here. Pages
are written in English; the review notes keep the language they were written in.
