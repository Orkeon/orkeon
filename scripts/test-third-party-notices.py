#!/usr/bin/env python3
"""test-third-party-notices.py -- Tests of scripts/third-party-notices.py.

The generator reads restored `project.assets.json` files and a NuGet cache; CI runs it for
real only after its restore. These tests run before any restore: each one builds a
throw-away tree -- two applications with their assets files, a package cache holding
nuspecs, license files and notices -- and drives the script's main() against it. Standard
library only, like the script under test.

Usage:
  python3 scripts/test-third-party-notices.py
  python3 scripts/test-third-party-notices.py -v
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from xml.sax.saxutils import escape

SCRIPTS = Path(__file__).resolve().parent


def _load_generator():
    spec = importlib.util.spec_from_file_location(
        "third_party_notices", SCRIPTS / "third-party-notices.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


tpn = _load_generator()

APPS = [("one", "src/apps/One/One.csproj"), ("two", "src/apps/Two/Two.csproj")]
WINDOWS_CACHE = "C:\\Users\\nobody\\.nuget\\packages\\"
HAND_WRITTEN = """# Third-Party Notices

Hand-written introduction, kept as it is.

---

## 1. Alpha.Lib

- **Version**: `1.0.0` (pinned)
- **License**: MIT
"""
MIT_CRLF = "\ufeffMIT License\r\n\r\nCopyright (c) Alpha\r\n\r\nPermission is hereby granted.\r\n"
NOTICE = "Beta Project\nCopyright 2020 The Beta Foundation.\n\nA line quoting ````code````.\n"
RTF = "{\\rtf1\\ansi Gamma notices\\par\n}\n\x00"


def _nuspec(package_id: str, version: str, license_xml: str = "", copyright_: str = "",
            project: str = "", repository: str = "") -> str:
    parts = [f"<id>{package_id}</id>", f"<version>{version}</version>", license_xml]
    if copyright_:
        parts.append(f"<copyright>{escape(copyright_)}</copyright>")
    if project:
        parts.append(f"<projectUrl>{project}</projectUrl>")
    if repository:
        parts.append(f'<repository type="git" url="{repository}" />')
    return ('<?xml version="1.0" encoding="utf-8"?>\n'
            '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
            f"<metadata>{''.join(parts)}</metadata></package>\n")


def _expression(spdx: str) -> str:
    return (f'<license type="expression">{spdx}</license>'
            f"<licenseUrl>https://licenses.nuget.org/{spdx}</licenseUrl>")


class Fixture:
    """A repository root, a package cache, and the two applications' assets files."""

    def __init__(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="third-party-notices-"))
        self.cache = self.root / "cache"
        self.home = self.root / "home"
        self.home.mkdir()
        self.write(tpn.NOTICES_FILE, HAND_WRITTEN)
        for _, csproj in APPS:
            self.write(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\" />\n")
        # one: a real cache folder; two: a Windows folder, found again through NUGET_PACKAGES.
        self.assets = {"one": self._assets(str(self.cache) + "/"), "two": self._assets(WINDOWS_CACHE)}
        self.package("Alpha.Lib", "1.0.0", _nuspec("Alpha.Lib", "1.0.0", _expression("MIT"),
                     "Copyright (c) Alpha", "https://example.org/alpha"), {"LICENSE.txt": MIT_CRLF},
                     apps=("one", "two"))
        self.package("Alpha.Extra", "1.0.0", _nuspec("Alpha.Extra", "1.0.0", _expression("MIT"),
                     "Copyright (c) Alpha", "https://example.org/alpha"),
                     {"LICENSE": "MIT License\n\nCopyright (c) Alpha\n\nPermission is hereby granted.\n"})
        self.package("Beta", "2.0.0", _nuspec("Beta", "2.0.0", '<license type="file">License.md</license>'
                     "<licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl>",
                     repository="https://example.org/beta.git"),
                     {"License.md": "Beta License\n\nAll rights granted.\n", "NOTICE": NOTICE})
        self.package("Gamma", "3.0.0", _nuspec("Gamma", "3.0.0", _expression("Apache-2.0"),
                     "Gamma | Authors <gamma@example.org>", "https://example.org/gamma"),
                     {"ThirdPartyNotices.rtf": RTF})
        self.package("Zeta.Native", "1.0.0", _nuspec("Zeta.Native", "1.0.0", _expression("MIT")),
                     {}, group="runtimeTargets",
                     assets={"runtimes/linux-x64/native/libzeta.so": {"assetType": "native", "rid": "linux-x64"}},
                     apps=("two",))
        # Never redistributed: build-only, meta-package, excluded asset (placeholder).
        self.package("Delta.Analyzers", "1.0.0", _nuspec("Delta.Analyzers", "1.0.0", _expression("MIT")),
                     {}, group="build", assets={"build/Delta.Analyzers.targets": {}})
        self.package("Epsilon.Meta", "1.0.0", _nuspec("Epsilon.Meta", "1.0.0", _expression("MIT")),
                     {}, group=None)
        self.package("Eta", "1.0.0", _nuspec("Eta", "1.0.0", _expression("MIT")), {},
                     assets={"lib/net10.0/_._": {}})
        self.assets["one"]["targets"]["net10.0"]["Orkeon.Own/1.0.0"] = {"type": "project"}
        self.save_assets()

    def write(self, rel: str, text: str) -> Path:
        path = self.root / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8", newline="")
        return path

    @staticmethod
    def _assets(folder: str) -> dict:
        return {"version": 3, "targets": {"net10.0": {}}, "libraries": {}, "packageFolders": {folder: {}}}

    def package(self, package_id: str, version: str, nuspec: str, files: dict[str, str],
                group: str | None = "runtime", assets: dict | None = None,
                apps: tuple[str, ...] = ("one",)) -> None:
        folder = self.cache / package_id.lower() / version
        folder.mkdir(parents=True)
        (folder / f"{package_id}.nuspec").write_text(nuspec, encoding="utf-8")
        for name, text in files.items():
            (folder / name).write_bytes(text.encode("utf-8"))
        key = f"{package_id}/{version}"
        target = {"type": "package"}
        if group is not None:
            target[group] = assets if assets is not None else {f"lib/net10.0/{package_id}.dll": {}}
        listed = sorted([f"{package_id}.nuspec", *files, f"{package_id.lower()}.{version}.nupkg.sha512"])
        for app in apps:
            self.assets[app]["targets"]["net10.0"][key] = target
            self.assets[app]["libraries"][key] = {"type": "package", "path": f"{package_id.lower()}/{version}",
                                                  "files": listed}

    def save_assets(self) -> None:
        for app, csproj in APPS:
            path = (self.root / csproj).parent / "obj-linux" / "project.assets.json"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(self.assets[app], indent=2), encoding="utf-8")

    def run(self, *argv: str, env: dict[str, str] | None = None) -> tuple[int, str, str]:
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = tpn.main(list(argv), root=self.root, apps=APPS,
                            env={"NUGET_PACKAGES": str(self.cache)} if env is None else env,
                            home=self.home, platform="linux")
        return code, out.getvalue(), err.getvalue()

    def notices(self) -> str:
        return (self.root / tpn.NOTICES_FILE).read_text(encoding="utf-8")


class GeneratorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.fx = Fixture()

    def tearDown(self) -> None:
        shutil.rmtree(self.fx.root, ignore_errors=True)

    def section(self) -> str:
        text = self.fx.notices()
        return text[text.index(tpn.BEGIN_MARKER):]

    def row(self, package_id: str) -> list[str]:
        rows = tpn.rows(self.section())
        return next(cells for (pid, _), cells in rows.items() if pid == package_id.lower())

    # --- the closure ----------------------------------------------------------------------

    def test_only_packages_that_put_a_file_in_the_output_are_listed(self) -> None:
        refs = tpn.closure(self.fx.root, APPS, platform="linux")
        self.assertEqual({"alpha.lib", "alpha.extra", "beta", "gamma", "zeta.native"},
                         {pid for pid, _ in refs})

    def test_each_package_names_the_applications_that_ship_it_in_their_order(self) -> None:
        refs = tpn.closure(self.fx.root, APPS, platform="linux")
        self.assertEqual(["one", "two"], refs[("alpha.lib", "1.0.0")].apps)
        self.assertEqual(["two"], refs[("zeta.native", "1.0.0")].apps)

    def test_the_assets_file_of_the_platform_comes_first_and_the_other_one_serves(self) -> None:
        project = self.fx.root / "src/apps/One"
        self.assertEqual("obj-linux", tpn.assets_file(project, "linux").parent.name)
        self.assertEqual("obj-linux", tpn.assets_file(project, "win32").parent.name)  # obj/ absent
        (project / "obj").mkdir()
        shutil.copy(project / "obj-linux/project.assets.json", project / "obj/project.assets.json")
        self.assertEqual("obj", tpn.assets_file(project, "win32").parent.name)
        self.assertEqual("obj-linux", tpn.assets_file(project, "linux").parent.name)

    # --- the package folders --------------------------------------------------------------

    def test_a_windows_package_folder_falls_back_to_nuget_packages(self) -> None:
        code, _, err = self.fx.run()
        self.assertEqual(0, code, err)
        self.assertIn("| Zeta.Native | 1.0.0 |", self.fx.notices())

    def test_then_to_the_home_cache(self) -> None:
        (self.fx.home / ".nuget").mkdir()
        self.fx.cache.rename(self.fx.home / ".nuget" / "packages")
        code, _, err = self.fx.run(env={})
        self.assertEqual(0, code, err)
        self.assertIn("| Zeta.Native | 1.0.0 |", self.fx.notices())

    def test_a_windows_path_is_also_tried_at_its_wsl_mount(self) -> None:
        if tpn.os.name == "nt":
            self.skipTest("the WSL mount exists only seen from Linux")
        self.assertEqual(Path("/mnt/c/Users/me/.nuget/packages"),
                         tpn._wsl_mount("C:\\Users\\me\\.nuget\\packages\\"))
        self.assertIsNone(tpn._wsl_mount("/home/me/.nuget/packages/"))

    def test_a_windows_folder_is_found_again_at_its_wsl_mount(self) -> None:
        # No NUGET_PACKAGES, no home cache: only the mount of the Windows folder has Zeta.Native.
        original = tpn._wsl_mount
        tpn._wsl_mount = lambda folder: self.fx.cache if folder == WINDOWS_CACHE else None
        try:
            code, _, err = self.fx.run(env={})
        finally:
            tpn._wsl_mount = original
        self.assertEqual(0, code, err)
        self.assertIn("| Zeta.Native | 1.0.0 |", self.fx.notices())

    def test_a_path_this_os_cannot_use_is_never_read_against_the_current_directory(self) -> None:
        if tpn.os.name == "nt":
            self.skipTest("a directory named like a Windows path exists only on POSIX")
        cwd = self.fx.root / "cwd"
        shutil.copytree(self.fx.cache, cwd / WINDOWS_CACHE)  # one directory, named C:\Users\...
        previous = Path.cwd()
        tpn.os.chdir(cwd)
        try:
            code, _, err = self.fx.run(env={})
        finally:
            tpn.os.chdir(previous)
        self.assertEqual(2, code)
        self.assertIn("Zeta.Native 1.0.0: not in any package folder", err)

    def test_a_package_in_no_folder_says_to_restore(self) -> None:
        code, _, err = self.fx.run(env={})
        self.assertEqual(2, code)
        self.assertIn("Zeta.Native 1.0.0: not in any package folder", err)
        self.assertIn("dotnet restore", err)

    def test_a_missing_assets_file_says_to_restore(self) -> None:
        shutil.rmtree(self.fx.root / "src/apps/Two/obj-linux")
        code, _, err = self.fx.run()
        self.assertEqual(2, code)
        self.assertIn("src/apps/Two: no obj-linux/ or obj/ project.assets.json", err)

    # --- what is written ------------------------------------------------------------------

    def test_written_then_checked_with_the_hand_written_part_kept(self) -> None:
        code, out, err = self.fx.run()
        self.assertEqual(0, code, err)
        self.assertIn("5 package versions written", out)
        text = self.fx.notices()
        self.assertTrue(text.startswith(HAND_WRITTEN.rstrip("\n") + "\n\n---\n\n" + tpn.BEGIN_MARKER))
        self.assertTrue(text.endswith(tpn.END_MARKER + "\n"))
        self.assertEqual((0, ""), self.fx.run("--check")[0::2])

    def test_the_output_is_deterministic(self) -> None:
        self.fx.run()
        first = self.fx.notices()
        self.fx.run()
        self.assertEqual(first, self.fx.notices())
        self.assertNotRegex(first, r"20\d\d-\d\d-\d\d")  # no date, no timestamp

    def test_each_row_carries_the_nuspec_license_copyright_and_project(self) -> None:
        self.fx.run()
        self.assertEqual(["Alpha.Lib (\u00a71)", "1.0.0", "MIT", "Copyright (c) Alpha",
                          "<https://example.org/alpha>", "one, two", "L1"], self.row("Alpha.Lib"))
        beta = self.row("Beta")
        self.assertEqual(["file `License.md`", tpn.NONE, "<https://example.org/beta.git>"], beta[2:5])
        self.assertEqual("L2, N1", beta[6])
        gamma = self.row("Gamma")
        self.assertEqual("Gamma \\| Authors \\<gamma@example.org\\>", gamma[3])

    def test_texts_are_copied_once_verbatim_and_fenced(self) -> None:
        self.fx.run()
        section = self.section()
        # Alpha.Lib's CRLF + BOM copy and Alpha.Extra's LF copy are one text.
        self.assertEqual(1, section.count("Permission is hereby granted."))
        self.assertIn("Shipped by 2 packages: Alpha.Extra 1.0.0 and Alpha.Lib 1.0.0.", section)
        self.assertNotIn("\r", section)
        self.assertNotIn("\ufeff", section)
        # A text holding a run of four backticks gets a fence of five.
        self.assertIn("`````text\n" + NOTICE.rstrip("\n") + "\n`````", section)
        # The RTF notice keeps its language, loses its NUL.
        self.assertIn("```rtf\n{\\rtf1\\ansi Gamma notices\\par\n}\n```", section)
        self.assertNotIn("\x00", section)
        self.assertLess(section.index("### License files"), section.index("#### N1 \u00b7 `NOTICE`"))

    # --- what --check reports -------------------------------------------------------------

    def test_check_without_markers_lists_every_package(self) -> None:
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn("has no generated inventory", err)
        self.assertIn("5 package version(s) -- no entry for", err)
        self.assertIn("Zeta.Native 1.0.0 (MIT; shipped in two)", err)

    def test_check_fails_when_the_closure_changes_without_regeneration(self) -> None:
        self.fx.run()
        self.fx.package("Theta", "1.0.0", _nuspec("Theta", "1.0.0", _expression("MIT")), {})
        self.fx.assets["one"]["targets"]["net10.0"].pop("Gamma/3.0.0")
        self.fx.save_assets()
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn("1 package version(s) -- no entry for\n      Theta 1.0.0", err)
        self.assertIn("no longer shipped:\n      Gamma 3.0.0", err)

    def test_check_fails_on_a_hand_edit_inside_the_markers(self) -> None:
        self.fx.run()
        path = self.fx.root / tpn.NOTICES_FILE
        path.write_text(self.fx.notices().replace("Beta Project", "Beta Projekt"), encoding="utf-8")
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn("the texts or the layout differ", err)
        self.assertIn("-Beta Projekt", err)

    def test_a_hand_written_version_that_is_not_shipped_fails(self) -> None:
        path = self.fx.root / tpn.NOTICES_FILE
        path.write_text(HAND_WRITTEN.replace("`1.0.0`", "`0.9.0`"), encoding="utf-8")
        code, _, err = self.fx.run()
        self.assertEqual(1, code)
        self.assertIn("section 1 (Alpha.Lib) states version 0.9.0; the shipped closure has 1.0.0", err)
        self.assertEqual(1, self.fx.run("--check")[0])

    def test_a_shipped_project_the_list_misses_fails(self) -> None:
        self.fx.write("scripts/package-installers.sh",
                      'APPS=(\n  "one|src/apps/One/One.csproj|One|true"\n'
                      '  "three|src/apps/Three/Three.csproj|Three|true|"\n)\n')
        self.fx.write("src/tools/Tool/Tool.csproj",
                      "<Project><PropertyGroup><PackAsTool>true</PackAsTool></PropertyGroup></Project>\n")
        self.fx.write("src/tools/Tool/obj/Copy.csproj", "<PackAsTool>true</PackAsTool>\n")  # build output
        self.fx.run()
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn("scripts/package-installers.sh ships src/apps/Three/Three.csproj", err)
        self.assertIn("a PackAsTool project ships src/tools/Tool/Tool.csproj", err)
        self.assertNotIn("Copy.csproj", err)

    def test_markers_out_of_order_are_refused(self) -> None:
        path = self.fx.root / tpn.NOTICES_FILE
        path.write_text(HAND_WRITTEN + tpn.END_MARKER + "\n" + tpn.BEGIN_MARKER + "\n", encoding="utf-8")
        code, _, err = self.fx.run()
        self.assertEqual(2, code)
        self.assertIn("markers are incomplete or out of order", err)


if __name__ == "__main__":
    unittest.main()
