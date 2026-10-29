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


class DocsCountTests(unittest.TestCase):
    """The counts written in prose and in trees on any docs page, against the code."""

    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="doc-claims-counts-"))
        self._saved_root = gate.ROOT
        gate.ROOT = self.root
        gate.ERRORS.clear()
        _git(self.root, "init", "-q")
        for n in ("Llm", "FileSystem", "Protocol"):
            self.write(f"src/constants/Orkeon.Constants.{n}/Orkeon.Constants.{n}.csproj", "<Project/>")
        refs = lambda names, kind: "".join(
            f'<ProjectReference Include="..\\..\\x\\Orkeon.{kind}.{n}\\Orkeon.{kind}.{n}.csproj" PrivateAssets="all" />'
            for n in names)
        self.write("src/packaging/Orkeon/Orkeon.csproj", f"<Project>{refs(['Llm', 'FileSystem'], 'Constants')}</Project>")
        self.write("src/packaging/Orkeon.Tools/Orkeon.Tools.csproj",
                   f"<Project>{refs(['Code', 'Data', 'Web'], 'Tools')}</Project>")
        _git(self.root, "add", "-A")

    def tearDown(self) -> None:
        gate.ROOT = self._saved_root
        gate.ERRORS.clear()
        shutil.rmtree(self.root, ignore_errors=True)

    def write(self, rel: str, text: str) -> None:
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def errors_for(self, text: str, rel: str = "docs/page.md") -> str:
        self.write(rel, text)
        gate.ERRORS.clear()
        gate._GIT_LISTING.clear()  # the listing is cached per ROOT; this test adds files between calls
        gate.check_docs_counts(tools=120, src_projects=50, test_projects=38)
        return "\n".join(gate.ERRORS)

    def test_truths_are_read_from_the_code(self) -> None:
        self.assertEqual(3, gate.gt_constants_satellites())
        self.assertEqual(2, gate.gt_umbrella_satellites())
        self.assertEqual(3, gate.gt_tool_families())

    def test_exact_claims_pass(self) -> None:
        text = ("120 built-in tool classes, **120** outils intégrés, 120 tools by category.\n"
                "src/    # 50 projects\ntests/  # 38 projets\n"
                "three built-in tool families, trois familles d'outils, the three `Orkeon.Tools.<family>` projects\n"
                "two `Orkeon.Constants.*` satellites, three zero-dependency constants satellites\n")
        self.assertEqual("", self.errors_for(text))

    def test_a_stale_built_in_tool_count_is_reported_in_both_languages(self) -> None:
        self.assertIn("docs/page.md:1", self.errors_for("102 built-in tools"))
        (self.root / "docs/page.md").unlink()
        self.assertIn("docs/fr/page.md:1", self.errors_for("102 outils intégrés", "docs/fr/page.md"))

    def test_a_stale_tool_total_by_category_is_reported(self) -> None:
        self.assertIn("'99'", self.errors_for("99 tools by category"))

    def test_the_tool_families_of_a_package_are_not_the_built_in_total(self) -> None:
        self.assertEqual("", self.errors_for("The three built-in tool families"))

    def test_a_stale_project_count_in_a_tree_is_reported(self) -> None:
        self.assertIn("'48'", self.errors_for("src/    # 48 projects"))
        self.assertIn("'36'", self.errors_for("tests/  # 36 projets"))

    def test_a_stale_satellite_count_is_reported_with_a_number_word(self) -> None:
        self.assertIn("'seven'", self.errors_for("the seven `Orkeon.Constants.*` satellites"))

    def test_a_stale_family_count_is_reported(self) -> None:
        self.assertIn("'eight'", self.errors_for("The eight built-in tool families"))
        self.assertIn("'huit'", self.errors_for("les huit familles d'outils"))
        self.assertIn("'nine'", self.errors_for("the nine `Orkeon.Tools.<family>` projects"))

    def test_a_singular_or_a_partitive_is_not_a_count(self) -> None:
        text = ("replace one built-in tool with yours; un outil intégré\n"
                "lives in a satellite, vit dans un satellite, one of the satellites, un des satellites\n")
        self.assertEqual("", self.errors_for(text))

    def test_the_adr_index_is_checked_like_any_page(self) -> None:
        self.assertIn("'seven'", self.errors_for("seven satellites", "docs/adr/README.md"))

    def test_adrs_keep_the_numbers_of_their_day(self) -> None:
        self.assertEqual("", self.errors_for("49 src and 102 built-in tools", "docs/adr/ADR-099.md"))
        self.assertEqual("", self.errors_for("102 built-in tools", "docs/fr/adr/ADR-099.md"))


if __name__ == "__main__":
    unittest.main()
