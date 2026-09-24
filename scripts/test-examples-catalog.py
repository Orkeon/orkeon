#!/usr/bin/env python3
"""test-examples-catalog.py — Tests of the use-case catalogue tooling (STUDIO-36).

Covers scripts/generate_examples_index.py (examples/INDEX.md + examples/usecases.json)
and the use-case checks of scripts/lint-example-configs.py, on a two-example catalogue
built in a temporary directory and on the repository's own. Standard library only,
like the scripts under test; when PyYAML happens to be installed, it also proves that
every committed usecase.yaml reads the same through a real YAML parser.

Usage:
  python3 scripts/test-examples-catalog.py
  python3 scripts/test-examples-catalog.py -v
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import re
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parent


def _load_lint():
    spec = importlib.util.spec_from_file_location(
        "lint_example_configs", SCRIPTS / "lint-example-configs.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


lint = _load_lint()
gen = lint.CATALOG      # the generator module, loaded once, by the lint

CONFIG = """\
process: sequential
agents:
  writer:
    role: "Writer"
    tools:
      - "web_search"
      - "file_write"
tasks:
  write_note:
    description: "Write a note."
    agent: writer
"""

SCRIPT = """\
// orkeon-example: {"process":"sequential","agents":1,"tasks":1,"tools":["json_tool"]}
import { pickTools } from "../_tools/index.ts";
"""


def sheet(mounts=(), importable=True, tags=(), text="") -> str:
    """A valid sheet in the documented layout; `text` fills every title and problem."""
    def texts(key: str) -> str:
        return f"{key}:\n" + "".join(f"  {lang}: {json.dumps(text)}\n" for lang in gen.LANGUAGES)
    listed = "".join(f'  - "{m}"\n' for m in mounts)
    return (texts("title") + texts("problem")
            + f"tags: {json.dumps(list(tags))}\n"
            + (f"mounts:\n{listed}" if mounts else "mounts: []\n")
            + f"importable: {'true' if importable else 'false'}\n")


def write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8", newline="\n")


def run_generator(*args: str) -> tuple[int, str]:
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        code = gen.main(["generate_examples_index.py", *args])
    return code, out.getvalue()


class CatalogueTestCase(unittest.TestCase):
    """A two-category catalogue: a YAML crew that ships data/ and writes files, and a
    TypeScript crew that imports the shared _tools/ module."""

    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="examples-catalog-"))
        self.addCleanup(shutil.rmtree, self.root)
        self.first = self.root / "01-alpha" / "01-first"
        self.second = self.root / "02-beta" / "02-second"
        write(self.root / "01-alpha" / "README.md", "# 01 - Alpha\n")
        write(self.first / "config.yaml", CONFIG)
        write(self.first / "README.md", "# 1. First\n")
        write(self.first / "data" / "sample.csv", "a,b\n1,2\n")
        write(self.first / gen.USECASE_FILE,
              sheet(mounts=("./data:/data:ro", "./output:/output:rw"), tags=("research",)))
        write(self.root / "02-beta" / "README.md", "# 02 - Beta\n")
        write(self.second / "main.ork.ts", SCRIPT)
        write(self.second / gen.USECASE_FILE, sheet(importable=False))

    def generate(self) -> None:
        code, out = run_generator("--root", str(self.root))
        self.assertEqual(0, code, out)

    def lint_messages(self, require_texts: bool = False) -> list[str]:
        _, found = lint.lint_use_cases(self.root, require_texts)
        return [f"{path.relative_to(self.root).as_posix()}: {f.msg}" for path, f in found]


class GeneratorTests(CatalogueTestCase):

    def test_two_runs_write_the_same_manifest(self) -> None:
        self.generate()
        first = (self.root / gen.MANIFEST_NAME).read_bytes()
        self.generate()
        second = (self.root / gen.MANIFEST_NAME).read_bytes()

        self.assertEqual(first, second)
        data = json.loads(first)
        self.assertEqual(first.decode("utf-8"),
                         json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True) + "\n")
        self.assertEqual(["01-first", "02-second"], [u["id"] for u in data["useCases"]])

    def test_an_entry_joins_the_sheet_to_what_the_crew_declares(self) -> None:
        cats = gen.collect_categories(self.root)
        manifest, problems = gen.build_manifest(cats)
        self.assertEqual([], problems)
        first, second = manifest["useCases"]

        self.assertEqual(
            {"id": "01-first", "category": "01-alpha", "number": 1, "format": "yaml",
             "process": "sequential", "agents": 1, "tasks": 1,
             "tools": ["file_write", "web_search"], "hasSampleData": True,
             "requiresNetwork": True, "requiresKeys": ["ORKEON_TAVILY_API_KEY"],
             "tags": ["research"], "mounts": ["./data:/data:ro", "./output:/output:rw"],
             "importable": True},
            {k: v for k, v in first.items() if k not in gen.TEXT_KEYS})
        self.assertEqual(dict.fromkeys(gen.LANGUAGES, ""), first["title"])
        self.assertEqual(("ork.ts", ["json_tool"], False, False, [], False),
                         (second["format"], second["tools"], second["hasSampleData"],
                          second["requiresNetwork"], second["requiresKeys"], second["importable"]))

    def test_usecase_sheets_leave_the_index_unchanged(self) -> None:
        with_sheets = gen.render(gen.collect_categories(self.root))
        for path in self.root.glob(f"*/*/{gen.USECASE_FILE}"):
            path.unlink()

        self.assertEqual(with_sheets, gen.render(gen.collect_categories(self.root)))

    def test_has_data_means_a_data_folder(self) -> None:
        write(self.second / "notes.txt", "not sample data\n")
        first, second = [e for c in gen.collect_categories(self.root) for e in c.examples]

        self.assertEqual((True, False), (first.has_data, second.has_data))

    def test_a_missing_sheet_keeps_the_manifest_unwritten(self) -> None:
        (self.second / gen.USECASE_FILE).unlink()
        code, out = run_generator("--root", str(self.root))

        self.assertEqual(1, code)
        self.assertIn("02-second/usecase.yaml: missing", out)
        self.assertTrue((self.root / gen.INDEX_NAME).is_file())
        self.assertFalse((self.root / gen.MANIFEST_NAME).exists())

    def test_an_unclassified_tool_stops_the_generator(self) -> None:
        write(self.first / "config.yaml", CONFIG.replace('"web_search"', '"teleport"'))
        _, problems = gen.build_manifest(gen.collect_categories(self.root))

        self.assertEqual(["tool 'teleport' is not classified — add it to TOOL_NEEDS in "
                          "scripts/generate_examples_index.py"], [msg for _, _, msg in problems])

    def test_check_reports_a_stale_manifest(self) -> None:
        self.generate()
        write(self.second / gen.USECASE_FILE, sheet(importable=False, tags=("finance",)))
        code, out = run_generator("--root", str(self.root), "--check")

        self.assertEqual(1, code)
        self.assertIn(f"{gen.MANIFEST_NAME} is stale", out)


class SheetReaderTests(unittest.TestCase):

    VALID = sheet(mounts=("./data:/data:ro", "./output:/output:rw"), tags=("research", "pdf"))

    @staticmethod
    def errors(text: str) -> str:
        data, lines, errors = gen.parse_sheet(text)
        return "\n".join(msg for _, msg in errors or gen.validate_sheet(data, lines))

    def test_reads_the_documented_layout(self) -> None:
        text = (
            "# A comment, then the sheet.\n"
            "title:\n"
            '  fr: "Résumer l\'actualité d\'un site, \\"chaque\\" matin"\n'
            '  en: "Summarize a site\'s news every morning"  # trailing comment\n'
            '  es: ""\n  de: ""\n'
            '  zh-Hans: "每天早上总结网站新闻"\n'
            "problem:\n" + "".join(f'  {lang}: ""\n' for lang in gen.LANGUAGES) +
            "\n"
            'tags: ["news", "daily-digest"]\n'
            "mounts:\n"
            '  - "./output:/output:rw"\n'
            "importable: false\n")
        data, lines, errors = gen.parse_sheet(text)

        self.assertEqual([], errors + gen.validate_sheet(data, lines))
        self.assertEqual('Résumer l\'actualité d\'un site, "chaque" matin', data["title"]["fr"])
        self.assertEqual("每天早上总结网站新闻", data["title"]["zh-Hans"])
        self.assertEqual((["news", "daily-digest"], ["./output:/output:rw"], False),
                         (data["tags"], data["mounts"], data["importable"]))
        self.assertEqual(4, lines[("title", "en")])

    def test_rejects_what_it_would_have_to_guess(self) -> None:
        cases = {   # name: (text replaced, its replacement, the error expected)
            "an unquoted text": ('fr: ""', "fr: Bonjour", 'expected a "double-quoted" string'),
            "a tab": ('  fr: ""', '\tfr: ""', "indent with spaces, not tabs"),
            "a deeper indent": ('  fr: ""', '    fr: ""', "indent its entries by exactly two"),
            "a missing language": ('  zh-Hans: ""\n', "", "'zh-Hans' is missing"),
            "an unknown key": ("importable:", "summary: []\nimportable:", "unknown key 'summary'"),
            "a key set twice": ("tags:", "importable: true\ntags:", "'importable' is set twice"),
            "a padded text": ('  en: ""', '  en: " Hello"', "no leading or trailing spaces"),
            "a yaml boolean": ("importable: true", "importable: yes", 'expected a "double-quoted"'),
            "a bad mount": ('"./data:/data:ro"', '"data:/data:ro"', "is not a team-relative mount"),
            "an escaping mount": ('"./data:/data:ro"', '"./../data:/data:ro"',
                                  "is not a team-relative mount"),
            "a twice-mounted root": ('"./output:/output:rw"', '"./out:/data:rw"',
                                     "/data is mounted twice"),
            "a display tag": ('"pdf"', '"PDF files"', "is not a lowercase English identifier"),
        }
        for name, (old, new, expected) in cases.items():
            with self.subTest(name):
                self.assertIn(old, self.VALID)
                self.assertIn(expected, self.errors(self.VALID.replace(old, new, 1)))

    def test_keys_go_in_the_documented_order(self) -> None:
        tags = 'tags: ["research", "pdf"]\n'
        reordered = tags + self.VALID.replace(tags, "")

        self.assertIn("keys go in the order title, problem, tags, mounts, importable",
                      self.errors(reordered))

    def test_the_valid_baseline_is_valid(self) -> None:
        self.assertEqual("", self.errors(self.VALID))


class LintTests(CatalogueTestCase):

    def test_a_generated_catalogue_is_clean(self) -> None:
        self.generate()

        self.assertEqual([], self.lint_messages())

    def test_flags_a_missing_sheet(self) -> None:
        self.generate()
        (self.first / gen.USECASE_FILE).unlink()

        self.assertIn("01-alpha/01-first/usecase.yaml: missing — every numbered example "
                      "carries one (see examples/README.md)", self.lint_messages())

    def test_flags_an_id_two_categories_share(self) -> None:
        twin = self.root / "02-beta" / "01-first"
        write(twin / "config.yaml", CONFIG)
        write(twin / gen.USECASE_FILE, sheet(mounts=("./output:/output:rw",)))

        self.assertIn("02-beta/01-first: id '01-first' is already the id of 01-alpha/01-first — "
                      "an example's folder name is its id, unique across categories",
                      self.lint_messages())

    def test_flags_a_stale_manifest(self) -> None:
        self.generate()
        write(self.first / gen.USECASE_FILE,
              sheet(mounts=("./data:/data:ro", "./output:/output:rw"), tags=("research", "csv")))

        self.assertEqual([f"{gen.MANIFEST_NAME}: missing or stale — run "
                          "'bash scripts/generate-examples-index.sh'"], self.lint_messages())
        self.generate()
        self.assertEqual([], self.lint_messages())

    def test_texts_are_required_only_when_switched_on(self) -> None:
        self.generate()
        self.assertEqual([], self.lint_messages(require_texts=False))
        self.assertIn("01-alpha/01-first/usecase.yaml: title: not written yet in "
                      "fr, en, es, de, zh-Hans", self.lint_messages(require_texts=True))

        write(self.first / gen.USECASE_FILE,
              sheet(mounts=("./data:/data:ro", "./output:/output:rw"), text="Un texte"))
        write(self.second / gen.USECASE_FILE, sheet(importable=False, text="Un texte"))
        self.generate()
        self.assertEqual([], self.lint_messages(require_texts=True))

    def test_a_sheet_must_agree_with_its_crew(self) -> None:
        write(self.first / gen.USECASE_FILE, sheet(mounts=("./data:/data:ro",)))
        write(self.second / gen.USECASE_FILE, sheet(mounts=("./data:/data:ro",), importable=True))
        messages = "\n".join(self.lint_messages())

        self.assertIn("01-first/usecase.yaml: the crew writes files (file_write) but no mount "
                      "is writable", messages)
        self.assertIn("02-second/usecase.yaml: mounts ./data, but the example ships no "
                      "data/ folder", messages)
        self.assertIn("02-second/usecase.yaml: importable: true, but the crew imports the shared "
                      "../_tools/ module", messages)


class RepositoryTests(unittest.TestCase):
    """The committed catalogue itself."""

    def test_committed_index_and_manifest_are_what_the_generator_writes(self) -> None:
        code, out = run_generator("--check")

        self.assertEqual(0, code, out)

    def test_the_manifest_lists_every_numbered_example_once(self) -> None:
        manifest = json.loads((gen.EXAMPLES / gen.MANIFEST_NAME).read_text(encoding="utf-8"))
        ids = [u["id"] for u in manifest["useCases"]]
        folders = sorted(e.path.name for c in gen.collect_categories() for e in c.examples)

        self.assertEqual(folders, sorted(ids))
        self.assertEqual(len(ids), len(set(ids)))

    def test_the_sheet_examples_readme_shows_is_valid(self) -> None:
        readme = (gen.EXAMPLES / "README.md").read_text(encoding="utf-8")
        section = readme.split("## Use-case sheet (`usecase.yaml`)", 1)[-1]
        block = re.search(r"```yaml\n(.*?)```", section, re.DOTALL)
        self.assertIsNotNone(block, "examples/README.md lost its sample sheet")
        data, lines, errors = gen.parse_sheet(block.group(1))

        self.assertEqual([], errors + gen.validate_sheet(data, lines))
        self.assertTrue(all(data["problem"].values()))

    def test_finance_module_tools_match_the_module(self) -> None:
        self.assertEqual(sorted(lint.module_tool_names(gen.EXAMPLES)),
                         sorted(gen.FINANCE_MODULE_TOOLS))

    def test_a_tool_that_needs_a_key_reaches_the_network(self) -> None:
        self.assertEqual([], [name for name, needs in gen.TOOL_NEEDS.items()
                              if needs.keys and not needs.network])

    def test_committed_sheets_read_the_same_through_a_yaml_parser(self) -> None:
        try:
            import yaml
        except ImportError:
            self.skipTest("PyYAML is not installed")
        sheets = sorted(gen.EXAMPLES.glob(f"*/*/{gen.USECASE_FILE}"))
        self.assertTrue(sheets)
        for path in sheets:
            with self.subTest(path.relative_to(gen.EXAMPLES).as_posix()):
                text = path.read_text(encoding="utf-8")
                data, _, errors = gen.parse_sheet(text)
                self.assertEqual([], errors)
                self.assertEqual(yaml.safe_load(text), data)


if __name__ == "__main__":
    unittest.main()
