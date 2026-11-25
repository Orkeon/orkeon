#!/usr/bin/env python3
"""Refuses a Markdown link whose #anchor names no heading of the Markdown file it points to.

docs.yml builds the site with `docfx --warningsAsErrors`, which refuses such a link (InvalidBookmark).
A heading's anchor is the one docfx and GitHub give it: the text of the heading in lower case, letters
(accented ones too), digits, "-" and "_" kept, a space made "-", everything else dropped ("d'un" gives
"dun"), "-1", "-2"... appended when the page has it already.
Reads every tracked *.md of the current repository; exits 1 with "path:line: link" per broken link.
"""

import os
import re
import subprocess
import sys
import unicodedata
from urllib.parse import unquote

FENCE = re.compile(r"^[ \t>]*(?:(?:[-*+]|\d+[.)])[ \t]+)?(`{3,}|~{3,})(.*)$")
HEADING = re.compile(r"^ {0,3}(?:> ?)*#{1,6}[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$")
EXPLICIT = re.compile(r"""<[a-zA-Z][^>]*?\b(?:id|name)\s*=\s*["']([^"']+)["']""")
CODE_SPAN = re.compile(r"(?<!`)(`+)(?!`)((?:(?!\n[ \t]*\n).)*?[^`])\1(?!`)", re.S)
COMMENT = re.compile(r"<!--.*?-->", re.S)
INLINE_LINK = re.compile(r"\]\(\s*<?([^)\s>#]*)#([^)\s>]+)>?(?:\s+(?:\"[^\"]*\"|'[^']*'))?\s*\)")
REFERENCE = re.compile(r"^ {0,3}\[[^\]]+\]:[ \t]*<?([^\s>#]*)#([^\s>]+)", re.M)


def blank(match: re.Match) -> str:
    """Nothing but the line ends of what matched, so that line numbers hold."""
    return re.sub(r"[^\n]", " ", match.group(0))


def without_fences(text: str) -> str:
    """The text with the lines of its fenced code blocks emptied."""
    kept, fence = [], None
    for line in text.split("\n"):
        found = FENCE.match(line)
        if fence is not None:
            if found and found[1].startswith(fence) and not found[2].strip():
                fence = None
            kept.append("")
        elif found and not (found[1][0] == "`" and "`" in found[2]):
            fence = found[1]
            kept.append("")
        else:
            kept.append(line)
    return "\n".join(kept)


def slug(heading: str) -> str:
    """The anchor of a heading's text."""
    parts = re.split(r"(`+[^`]*`+)", heading)  # what a code span holds is kept as it is
    for i in range(0, len(parts), 2):
        part = re.sub(r"!?\[([^\]]*)\](?:\([^)]*\)|\[[^\]]*\])", r"\1", parts[i])
        part = re.sub(r"<[^>]+>", "", part)
        parts[i] = re.sub(r"(?<!\w)_+|_+(?!\w)", "", part)  # emphasis; a "_" inside a word stays
    text = "".join(parts).strip()
    return "".join(
        "-" if c == " " else c.lower()
        for c in text
        if c in " -_" or unicodedata.category(c)[0] == "L" or unicodedata.category(c) == "Nd"
    )


def anchors(text: str) -> set[str]:
    """Every anchor a page offers."""
    text = COMMENT.sub(blank, without_fences(text))
    found, lines, previous = set(EXPLICIT.findall(text)), text.split("\n"), ""
    for line in lines:
        heading = HEADING.match(line)
        title = heading[1] if heading else None
        if title is None and re.match(r"^ {0,3}(=+|-{2,})[ \t]*$", line) and previous.strip():
            title = previous  # a setext heading; a table or front matter adds an anchor no one links to
        if title is not None:
            base = name = slug(title)
            n = 0
            while name in found:
                n += 1
                name = f"{base}-{n}"
            found.add(name)
        previous = line
    return found


def main() -> int:
    listed = subprocess.run(
        ["git", "ls-files", "-z", "--", "*.md"], capture_output=True, check=True
    ).stdout.decode("utf-8")
    pages = {}
    for path in filter(None, listed.split("\0")):
        if os.path.isfile(path):
            with open(path, encoding="utf-8") as page:
                pages[os.path.normpath(path)] = page.read()
    known: dict[str, set[str]] = {}
    broken, read = [], 0
    for path, text in pages.items():
        prose = CODE_SPAN.sub(blank, COMMENT.sub(blank, without_fences(text)))
        for link in (*INLINE_LINK.finditer(prose), *REFERENCE.finditer(prose)):
            target, anchor = unquote(link[1]), unquote(link[2])
            if "://" in target or target.startswith(("mailto:", "/")):
                continue
            page = os.path.normpath(os.path.join(os.path.dirname(path), target)) if target else path
            if page not in pages:
                continue  # not a tracked Markdown page: a source file, a missing file, another repository
            read += 1
            if page not in known:
                known[page] = anchors(pages[page])
            if anchor not in known[page]:
                broken.append((path, prose.count("\n", 0, link.start()) + 1, f"{target}#{anchor}"))
    for path, line, link in sorted(broken):
        print(f"{path}:{line}: {link}")
    if not broken:
        print(f"Doc anchors OK: {read} links.")
    return 1 if broken else 0


if __name__ == "__main__":
    sys.exit(main())
