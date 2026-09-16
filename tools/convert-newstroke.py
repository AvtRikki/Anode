#!/usr/bin/env python3
"""Extracts KiCad's newstroke glyph table into src/render/Anode.Render/Fonts/newstroke.txt.gz.

Each line of the output is one glyph in Hershey encoding; line N is code point U+0020 + N.
Source: common/newstroke_font.cpp from the KiCad repository at a pinned commit
(GPL-2.0-or-later; CJK part MIT and SIL OFL 1.1 — see THIRD-PARTY-NOTICES.md).

Usage: tools/convert-newstroke.py [path/to/newstroke_font.cpp]
"""
import gzip
import os
import re
import sys
import urllib.request

COMMIT = "a46f62841996fbc9edac9b9daef348651a1b1496"
URL = f"https://gitlab.com/kicad/code/kicad/-/raw/{COMMIT}/common/newstroke_font.cpp"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "render", "Anode.Render", "Fonts", "newstroke.txt.gz")

LITERAL = re.compile(r'^\s*"((?:[^"\\]|\\.)*)"\s*,?\s*(?:/\*.*\*/)?\s*$')


def unescape(s: str) -> str:
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == "\\":
            nxt = s[i + 1]
            if nxt not in '\\"':
                raise ValueError(f"unexpected escape \\{nxt} in {s!r}")
            out.append(nxt)
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def main() -> None:
    if len(sys.argv) > 1:
        with open(sys.argv[1], encoding="utf-8") as f:
            source = f.read()
    else:
        # curl is used by the rest of the tooling; fall back to urllib if it is missing.
        import subprocess
        try:
            source = subprocess.run(["curl", "-fsSL", URL], check=True, capture_output=True, text=True).stdout
        except (OSError, subprocess.CalledProcessError):
            source = urllib.request.urlopen(URL).read().decode("utf-8")

    start = source.index("newstroke_font[] =")
    end = source.index("};", start)
    glyphs = []
    for line in source[start:end].splitlines():
        m = LITERAL.match(line)
        if m:
            glyph = unescape(m.group(1))
            if len(glyph) % 2 != 0 or len(glyph) < 2:
                raise ValueError(f"glyph {len(glyphs)} has odd length: {glyph!r}")
            glyphs.append(glyph)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    data = ("\n".join(glyphs) + "\n").encode("ascii")
    with gzip.GzipFile(OUT, "wb", compresslevel=9, mtime=0) as f:
        f.write(data)

    print(f"{len(glyphs)} glyphs (U+0020..U+{0x20 + len(glyphs) - 1:04X}), "
          f"{len(data) / 1024:.0f} KiB raw, {os.path.getsize(OUT) / 1024:.0f} KiB gzip -> {OUT}")


if __name__ == "__main__":
    main()
