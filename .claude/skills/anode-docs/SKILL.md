---
name: anode-docs
description: Keep Anode's README.md and docs/ true after a change. Use whenever work in this repository adds, changes or removes behaviour, a file format detail, UI, a module, the plugin contract, a dependency or the way tests are run — before committing.
---

# Keeping the documentation true

A change and its documentation belong in **one commit**. A feature nobody wrote down did not happen; a line that the
code no longer does is worse than no line at all.

## Where each kind of change goes

| The change | Write it in |
|---|---|
| What the program is, how to build, run or test it; the top-level layout; the plugin manifest example | `README.md` |
| A module, a seam between modules, the SDK, how a plugin is loaded or what it may contribute | `docs/architecture.md` |
| Reading, writing or drawing a KiCad file: a format detail, a fidelity rule, a gap closed or found | `docs/kicad-support.md` |
| Anything visible on screen: a panel, a shortcut, an inspector row, a check, a command | `docs/using.md` |
| How the suite is laid out or run, fixtures, snapshots | `docs/testing.md` |
| A plan being carried out — stages, what is done and what is left | `docs/design/*.md` |
| A decision taken with measurements behind it, or one reversed | `docs/adr/*.md` (a new numbered file) |
| A third-party library, a ported algorithm, borrowed data | `THIRD-PARTY-NOTICES.md` |

One fact lives in one place. The others link to it rather than repeating it.

## Rules

- **Say what is not done.** Every page has its "not done" lines. When a gap closes, delete that line in the same
  commit; when the work reveals a new one, add it. A gap written down is a decision; an unwritten one is a trap.
- **Numbers must be true.** `contractVersion` in the README's example, the pinned SDK version, package versions,
  sizes and paths are all checkable — check them rather than remembering them.
- **No counts that rot.** Never write how many tests there are, or how many nets a demo has, unless the number is the
  point of a measurement (then say which file and when).
- **Plain words.** Say what the thing does, in the voice the rest of the page uses. No adjectives about how good it
  is, no "simply", no future tense for what is already there.
- **English.** The documentation and the code are in English, whatever language the conversation is in.
- **Nothing private.** No absolute paths from this machine, no scratch directories, no personal names.

## Before committing

1. Name the change in one sentence, then decide which of the files above that sentence belongs in. If none, ask
   whether the change is really invisible — a behaviour change almost never is.
2. Update those files. Delete what is no longer true while you are there.
3. If the SDK changed in a way a plugin must be rebuilt for: bump `PlatformContract.Version` and both
   `plugin.json` manifests, and the README's example.
4. Check the numbers you touched:

   ```bash
   grep -rn "contractVersion" src/plugins/*/plugin.json README.md
   grep -rn "public const int Version" src/app/Anode.Sdk/Plugins.cs
   ```

5. Build and run the suite; the documentation is part of the change, not a follow-up.

   ```bash
   dotnet build Anode.slnx && dotnet test --solution Anode.slnx
   ```

6. Commit code and documentation together, with a message that says what changed and why.

## When a document is the wrong shape

If a change does not fit any page — a new domain, a new kind of artefact — add a page to `docs/` and a row to
`docs/README.md`'s table in the same commit. Keep it to the four questions the other pages answer: what it is, how it
works, what it does not do, and where the code is.
