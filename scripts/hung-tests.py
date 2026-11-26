#!/usr/bin/env python3
"""Names the tests a run left unfinished, from the diagnostic logs of Microsoft.Testing.Platform.

A module started with `--diagnostic --diagnostic-output-directory <dir>` logs every state a test
takes. A test that hangs is one the log shows in progress and never shows ended: nothing else
names it, since the terminal prints a test once it has ended and a job stopped at its time limit
prints nothing at all.

    python3 scripts/hung-tests.py <dir>          # one line per unfinished test, grouped by module
    python3 scripts/hung-tests.py --self-test

Exit 0 whatever it finds: it reports, the job that calls it has failed already.
"""
import pathlib
import re
import sys

# One state change of one test: its id, its name, then the state among its properties.
UPDATE = re.compile(
    r"TestNode = TestNode \{ Uid = TestNodeUid \{ Value = (?P<uid>[^ }]+) \}, "
    r"DisplayName = (?P<name>.*?), Properties = \[(?P<properties>.*)")
STATE = re.compile(r"\b(\w+)TestNodeStateProperty\b")


def unfinished(text):
    """The names of the tests `text` shows in progress and never ended, in the order they started."""
    started, ended = {}, set()
    for line in text.splitlines():
        update = UPDATE.search(line)
        if not update:
            continue
        states = set(STATE.findall(update["properties"]))
        if states - {"InProgress", "Discovered"}:
            ended.add(update["uid"])
        elif "InProgress" in states:
            started.setdefault(update["uid"], update["name"])
    return [name for uid, name in started.items() if uid not in ended]


def report(folder):
    logs = sorted(pathlib.Path(folder).glob("**/*.diag"))
    if not logs:
        print(f"no diagnostic log under {folder}: the modules were not started with --diagnostic")
        return
    found = 0
    for log in logs:
        names = unfinished(log.read_text(encoding="utf-8", errors="replace"))
        if names:
            print(f"{log.name}: {len(names)} test(s) started and never ended")
            for name in names:
                print(f"  {name}")
            found += len(names)
    if not found:
        print(f"{len(logs)} diagnostic log(s), no unfinished test: every test that started has ended")


def self_test():
    def line(uid, name, state):
        return (f"x TRACE TestNodeUpdateMessage {{ TestNode = TestNode {{ Uid = TestNodeUid {{ Value = {uid} }}, "
                f"DisplayName = {name}, Properties = [ Location {{ }}, {state}TestNodeStateProperty {{ }} ] }} }}")

    text = "\n".join([
        line("a", "Tests.Ends", "InProgress"),
        line("b", "Tests.Hangs(text: \"a, b\")", "InProgress"),
        line("c", "Tests.Fails", "InProgress"),
        line("a", "Tests.Ends", "Passed"),
        line("c", "Tests.Fails", "Failed"),
        line("d", "Tests.Skipped", "Skipped"),
        "noise without a test",
    ])
    got = unfinished(text)
    want = ['Tests.Hangs(text: "a, b")']
    if got != want:
        print(f"self-test FAILED: {got!r}, expected {want!r}")
        return 1
    print("self-test ok")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    if sys.argv[1] == "--self-test":
        sys.exit(self_test())
    report(sys.argv[1])
