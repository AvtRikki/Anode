# Working in this repository

Anode is a desktop ECAD application on the KiCad file formats: a workbench plus domain plugins. `README.md` says
what it is and how to build it; `docs/` holds the rest, starting at `docs/README.md`.

## Build and test

```bash
dotnet build Anode.slnx
```

```bash
dotnet test --solution Anode.slnx
```

Fixture tests need KiCad's demo files: `./tools/fetch-fixtures.sh` (not committed; without them those tests skip).

## The rules that matter here

- **Lossless round trip.** Reading and writing a file unchanged reproduces its bytes, and an edit that is undone
  restores them. Every edit carries a test of it.
- **KiCad's behaviour is measured, not guessed.** When a rule is in doubt, read KiCad's source or measure every
  fixture, and say in the code comment what was found.
- **One fact, one place.** The CST is the source of truth for a file; the typed model is a view over it.
- **Documentation ships with the change.** See the `anode-docs` skill: a change to behaviour, formats, UI, the SDK
  contract or the test layout updates `README.md` or the right page of `docs/` in the same commit.
- **English in the code and the documents**, whatever language the conversation is in.
