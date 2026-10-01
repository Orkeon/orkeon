#!/usr/bin/env python3
"""test-check-doc-claims.py -- Tests of the file enumeration of scripts/check-doc-claims.py.

The gate reads what git would publish: tracked files and new files not yet staged, never
a path .gitignore excludes. A working clone holds ignored trees a CI clone does not (the
third-party checkouts under examples/others/, ~34,000 files) -- walking them cost more
than ten minutes and some 361,000 false reports, locally only, where the gate is needed.

Each test builds a throw-away git repository, points the module's ROOT at it and asks the
enumeration helpers what they see. Standard library only, like the script under test.

Usage:
  python3 scripts/test-check-doc-claims.py
  python3 scripts/test-check-doc-claims.py -v
"""
from __future__ import annotations

import importlib.util
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent


def _load_gate():
    spec = importlib.util.spec_from_file_location(
        "check_doc_claims", SCRIPTS / "check-doc-claims.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


gate = _load_gate()

LEAK = "See backstage/tasks/secret.md for the plan.\n"


def _git(root: Path, *args: str) -> None:
    subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", *args],
                   cwd=root, check=True, capture_output=True)


class GitEnumerationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="doc-claims-"))
        self._saved_root = gate.ROOT
        gate.ROOT = self.root
        gate.ERRORS.clear()
        _git(self.root, "init", "-q")
        self.write(".gitignore", "/examples/others/*\n!/examples/others/README.md\n/docs/scratch.md\n/src/Lib/Local.cs\n")
        self.write("docs/tracked.md", LEAK)
        self.write("examples/others/README.md", "Third-party corpora.\n")
        self.write("src/Lib/Tracked.cs", "// tracked\n")
        _git(self.root, "add", "-A")
        _git(self.root, "commit", "-q", "-m", "seed")
        # After the commit: one new file git would add, two it never will.
        self.write("docs/new.md", LEAK)
        self.write("docs/scratch.md", LEAK)
        self.write("examples/others/vendor/README.md", LEAK)
        self.write("examples/others/vendor/src/Vendor.cs", "// ignored\n")
        self.write("src/Lib/obj/Generated.cs", "// build output\n")
        self.write("src/Lib/Local.cs", "// ignored\n")
        self.write("src/Lib/New.cs", "// new\n")

    def tearDown(self) -> None:
        gate.ROOT = self._saved_root
        gate.ERRORS.clear()
        shutil.rmtree(self.root, ignore_errors=True)

    def write(self, rel: str, text: str) -> None:
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def rels(self, paths) -> set[str]:
        return {Path(p).relative_to(self.root).as_posix() if Path(p).is_absolute() else str(p)
                for p in paths}

    def test_git_files_lists_tracked_and_new_files_never_ignored_ones(self) -> None:
        seen = self.rels(gate.git_files(self.root / "src", (".cs",)))
        self.assertEqual({"src/Lib/Tracked.cs", "src/Lib/New.cs"}, seen)

    def test_known_md_files_skip_ignored_paths(self) -> None:
        seen = set(gate.known_md_files())
        self.assertIn("docs/tracked.md", seen)
        self.assertIn("docs/new.md", seen)
        self.assertIn("examples/others/README.md", seen)
        self.assertNotIn("docs/scratch.md", seen)
        self.assertNotIn("examples/others/vendor/README.md", seen)

    def test_a_false_claim_in_an_ignored_file_is_not_reported(self) -> None:
        gate.check_private_submodule_leaks()
        reported = "\n".join(gate.ERRORS)
        self.assertNotIn("docs/scratch.md", reported)
        self.assertNotIn("examples/others/vendor", reported)

    def test_a_false_claim_in_a_new_unstaged_file_is_reported(self) -> None:
        gate.check_private_submodule_leaks()
        reported = "\n".join(gate.ERRORS)
        self.assertIn("docs/new.md:1", reported)
        self.assertIn("docs/tracked.md:1", reported)

    def test_a_deleted_but_still_tracked_file_is_not_listed(self) -> None:
        (self.root / "src/Lib/Tracked.cs").unlink()
        seen = self.rels(gate.git_files(self.root / "src", (".cs",)))
        self.assertEqual({"src/Lib/New.cs"}, seen)


if __name__ == "__main__":
    unittest.main()
