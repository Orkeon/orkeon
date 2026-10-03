#!/usr/bin/env python3
"""test-third-party-notices.py -- Tests of scripts/third-party-notices.py.

The generator reads restored `project.assets.json` files and a NuGet cache; CI runs it for
real only after its restore. These tests run before any restore: each one builds a
throw-away tree -- two applications with their assets files, a package cache holding
nuspecs, license files, notices and runtime packs, and as a test needs them Dockerfiles, a
.dockerignore, a Directory.Packages.props, publish folders -- and drives the script's main()
against it. Standard library only, like the script under test.

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
# A runtime pack's root, as a self-contained publish leaves it in the cache. CRLF on purpose:
# the copy is byte for byte, never normalised like the inventory's texts.
RUNTIME_PACK_FILES = {
    "LICENSE.TXT": b"The MIT License (MIT)\r\n\r\nCopyright (c) .NET Foundation and Contributors\r\n",
    "THIRD-PARTY-NOTICES.TXT": b".NET Runtime uses third-party libraries.\r\n\r\nLicense notice for zlib\r\n",
    "Icon.png": b"\x89PNG\r\n\x1a\n",
    "PACKAGE.md": b"## About\n",
}
NETCORE_LINUX = "Microsoft.NETCore.App.Runtime.linux-x64"
DOCKER_BUILD = ("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build\nCOPY . .\n"
                "RUN dotnet publish src/apps/One/One.csproj \\\n      -c Release -o /app\n")
DOCKER_RUNTIME = "FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime\nCOPY --from=build /app .\n"
DOCKER_NOTICES = "COPY LICENSE.md THIRD-PARTY-NOTICES.md /usr/share/doc/orkeon/\n"
IGNORE_MD = "**/bin\n*.md\n!README.md\n"
IGNORE_KEPT = IGNORE_MD + "!LICENSE.md\n!THIRD-PARTY-NOTICES.md\n"


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


def _deps(*packs: str, rid: str = "linux-x64") -> str:
    """The *.deps.json of a publish bundling these runtime packs ("<pack>/<version>")."""
    target = f".NETCoreApp,Version=v10.0/{rid}"
    libraries = {"App/1.0.0": {"type": "project", "serviceable": False, "sha512": ""}}
    libraries.update({f"runtimepack.{pack}": {"type": "runtimepack", "serviceable": False, "sha512": ""}
                      for pack in packs})
    return json.dumps({"runtimeTarget": {"name": target, "signature": ""},
                       "targets": {target: {key: {} for key in libraries}},
                       "libraries": libraries}, indent=2)


def _props(*pins: tuple[str, str]) -> str:
    items = "".join(f'\n    <PackageVersion Include="{i}" Version="{v}" />' for i, v in pins)
    return f"<Project>\n  <ItemGroup>{items}\n  </ItemGroup>\n</Project>\n"


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

    def runtime_pack(self, pack: str, version: str, files: dict[str, bytes] | None = None,
                     cache: Path | None = None) -> Path:
        """A runtime pack where a self-contained publish restores it: a package folder, with
        the nuspec under the pack's own casing."""
        folder = (self.cache if cache is None else cache) / pack.lower() / version
        folder.mkdir(parents=True)
        (folder / f"{pack}.nuspec").write_text(_nuspec(pack, version, _expression("MIT")), encoding="utf-8")
        for name, data in (RUNTIME_PACK_FILES if files is None else files).items():
            (folder / name).write_bytes(data)
        return folder

    def published(self, name: str, *packs: str, rid: str = "linux-x64",
                  included: list[tuple[str, str]] | None = None) -> Path:
        """The folder `dotnet publish -o` wrote: a deps.json bundling these runtime packs, a
        runtimeconfig.json listing `included` as bundled frameworks (self-contained), or
        naming its shared framework when there are none (framework-dependent)."""
        folder = self.root / "publish" / name
        folder.mkdir(parents=True)
        (folder / f"{name}.deps.json").write_text(_deps(*packs, rid=rid), encoding="utf-8")
        options = ({"includedFrameworks": [{"name": n, "version": v} for n, v in included]} if included
                   else {"framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}})
        (folder / f"{name}.runtimeconfig.json").write_text(
            json.dumps({"runtimeOptions": {"tfm": "net10.0", **options}}, indent=2), encoding="utf-8")
        return folder

    def runtime_notices(self, publish: Path, to: Path,
                        env: dict[str, str] | None = None) -> tuple[int, str, str]:
        """--runtime-notices for application one, with no NUGET_PACKAGES unless `env` says."""
        return self.run("--runtime-notices", str(publish), "--project", str(self.root / APPS[0][1]),
                        "--to", str(to), env={} if env is None else env)

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

    # --- what the container images carry --------------------------------------------------

    def test_an_image_that_does_not_copy_the_notices_fails_and_so_does_its_context(self) -> None:
        self.fx.write("deploy/Dockerfile.host", DOCKER_BUILD + DOCKER_RUNTIME)
        self.fx.write(".dockerignore", IGNORE_MD)
        self.fx.run()
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn("deploy/Dockerfile.host publishes an application, and its image does not copy "
                      "LICENSE.md and THIRD-PARTY-NOTICES.md to /usr/share/doc/orkeon/", err)
        self.assertIn(".dockerignore keeps LICENSE.md and THIRD-PARTY-NOTICES.md out of the build "
                      "context of deploy/Dockerfile.host", err)

    def test_an_image_that_copies_both_files_from_its_context_passes(self) -> None:
        self.fx.write(".dockerignore", IGNORE_KEPT)
        self.fx.write("Dockerfile", DOCKER_BUILD + DOCKER_RUNTIME + DOCKER_NOTICES)
        # The exec form, and one file at a time to a file destination, land the same files.
        self.fx.write("deploy/Dockerfile.host", DOCKER_BUILD + DOCKER_RUNTIME
                      + 'COPY ["LICENSE.md", "/usr/share/doc/orkeon/"]\n'
                      + "COPY --chmod=644 ./THIRD-PARTY-NOTICES.md /usr/share/doc/orkeon/THIRD-PARTY-NOTICES.md\n")
        # An image that publishes no application redistributes none of it: not checked.
        self.fx.write("Dockerfile.runners", "FROM busybox\nRUN true\n")
        self.fx.run()
        self.assertEqual((0, ""), self.fx.run("--check")[0::2])

    def test_only_a_copy_the_final_image_inherits_counts(self) -> None:
        self.fx.write(".dockerignore", IGNORE_KEPT)
        # Copied in the build stage: the image never sees it.
        self.fx.write("Dockerfile.runners", DOCKER_BUILD + DOCKER_NOTICES + DOCKER_RUNTIME)
        self.fx.run()
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn("Dockerfile.runners publishes an application, and its image does not copy", err)
        # Copied in the stage the final one is built FROM, as Dockerfile.runners does: inherited.
        self.fx.write("Dockerfile.runners", DOCKER_BUILD + DOCKER_RUNTIME + DOCKER_NOTICES
                      + "FROM runtime AS local-llm\nRUN true\n\nFROM runtime\n")
        self.assertEqual((0, ""), self.fx.run("--check")[0::2])
        # A directory destination without its slash makes a file of that name: nothing lands.
        self.fx.write("Dockerfile.runners", DOCKER_BUILD + DOCKER_RUNTIME
                      + "COPY LICENSE.md /usr/share/doc/orkeon\nCOPY THIRD-PARTY-NOTICES.md /usr/share/doc/orkeon\n")
        self.assertEqual(1, self.fx.run("--check")[0])

    def test_a_dockerignore_that_keeps_one_of_them_out_fails(self) -> None:
        self.fx.write("Dockerfile.runners", DOCKER_BUILD + DOCKER_RUNTIME + DOCKER_NOTICES)
        self.fx.write(".dockerignore", IGNORE_MD + "!LICENSE.md\n")
        self.fx.run()
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn(".dockerignore keeps THIRD-PARTY-NOTICES.md out of the build context of "
                      "Dockerfile.runners: add !THIRD-PARTY-NOTICES.md", err)
        self.assertNotIn("does not copy", err)

    def test_the_dockerignore_patterns_read_as_docker_reads_them(self) -> None:
        excludes = tpn.dockerignore_excludes
        self.assertTrue(excludes("*.md\n", "LICENSE.md"))
        self.assertTrue(excludes("**/*.md\n", "LICENSE.md"))
        self.assertTrue(excludes("/LICENSE.md\n", "LICENSE.md"))
        self.assertTrue(excludes("LICEN?E.[mM]d\n", "LICENSE.md"))
        self.assertFalse(excludes("*.md\n!LICENSE.md\n", "LICENSE.md"))
        self.assertTrue(excludes("!LICENSE.md\n*.md\n", "LICENSE.md"))  # the last match decides
        self.assertFalse(excludes("# *.md\ndocs/*.md\nLICENSE.mdx\n", "LICENSE.md"))

    # --- the .NET runtime a self-contained publish bundles --------------------------------

    def test_a_self_contained_publish_gets_its_runtime_pack_notices_byte_for_byte(self) -> None:
        pack = self.fx.runtime_pack(NETCORE_LINUX, "10.0.9")
        to = self.fx.root / "stage" / "licenses"
        code, out, err = self.fx.runtime_notices(self.fx.published("orkeon", f"{NETCORE_LINUX}/10.0.9"), to)
        self.assertEqual(0, code, err)
        self.assertEqual([NETCORE_LINUX], [p.name for p in to.iterdir()])
        copied = to / NETCORE_LINUX
        self.assertEqual(["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"], sorted(p.name for p in copied.iterdir()))
        for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"):
            self.assertEqual((pack / name).read_bytes(), (copied / name).read_bytes())
        self.assertIn(f"{NETCORE_LINUX} 10.0.9", out)

    def test_two_packs_give_two_folders_and_one_pack_twice_one_copy(self) -> None:
        netcore, desktop = "Microsoft.NETCore.App.Runtime.win-x64", "Microsoft.WindowsDesktop.App.Runtime.win-x64"
        for pack in (netcore, desktop):
            self.fx.runtime_pack(pack, "10.0.9")
        to = self.fx.root / "stage" / "licenses"
        studio = self.fx.published("studio", f"{netcore}/10.0.9", f"{desktop}/10.0.9", rid="win-x64")
        cli = self.fx.published("orkeon", f"{netcore}/10.0.9", rid="win-x64")
        (cli / "orkeon.other.deps.json").write_text(_deps(f"{netcore}/10.0.9", rid="win-x64"), encoding="utf-8")
        self.assertEqual(0, self.fx.runtime_notices(studio, to)[0])
        code, out, err = self.fx.runtime_notices(cli, to)
        self.assertEqual(0, code, err)
        self.assertEqual(1, out.count(f"{netcore} 10.0.9:"))
        self.assertEqual([netcore, desktop], sorted(p.name for p in to.iterdir()))
        for pack in (netcore, desktop):
            self.assertEqual(["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"], sorted(p.name for p in (to / pack).iterdir()))

    def test_a_framework_dependent_publish_copies_nothing(self) -> None:
        to = self.fx.root / "stage" / "licenses"
        code, out, err = self.fx.runtime_notices(self.fx.published("orkeon-slim"), to)
        self.assertEqual((0, ""), (code, err))
        self.assertFalse(to.exists())
        self.assertIn("nothing to copy", out)

    def test_a_runtime_pack_is_looked_for_where_the_inventory_looks(self) -> None:
        # Not in the assets file's folder: NUGET_PACKAGES, then the home cache.
        elsewhere = self.fx.root / "elsewhere"
        self.fx.runtime_pack(NETCORE_LINUX, "10.0.9", cache=elsewhere)
        publish = self.fx.published("orkeon", f"{NETCORE_LINUX}/10.0.9")
        code, _, err = self.fx.runtime_notices(publish, self.fx.root / "a", env={"NUGET_PACKAGES": str(elsewhere)})
        self.assertEqual(0, code, err)
        (self.fx.home / ".nuget").mkdir()
        elsewhere.rename(self.fx.home / ".nuget" / "packages")
        code, _, err = self.fx.runtime_notices(publish, self.fx.root / "b")
        self.assertEqual(0, code, err)
        self.assertTrue((self.fx.root / "b" / NETCORE_LINUX / "LICENSE.TXT").is_file())

    def test_a_pack_in_no_folder_stops_the_packaging_and_says_how_to_get_it(self) -> None:
        code, _, err = self.fx.runtime_notices(
            self.fx.published("orkeon", "Microsoft.NETCore.App.Runtime.osx-arm64/10.0.9", rid="osx-arm64"),
            self.fx.root / "licenses")
        self.assertEqual(2, code)
        self.assertIn("Microsoft.NETCore.App.Runtime.osx-arm64 10.0.9", err)
        self.assertIn("dotnet publish", err)
        self.assertIn("restore", err)
        self.assertFalse((self.fx.root / "licenses").exists())

    def test_a_pack_without_its_license_and_notices_stops_the_packaging(self) -> None:
        self.fx.runtime_pack(NETCORE_LINUX, "10.0.9", {"Icon.png": b"\x89PNG", "PACKAGE.md": b"# x\n"})
        code, _, err = self.fx.runtime_notices(
            self.fx.published("orkeon", f"{NETCORE_LINUX}/10.0.9"), self.fx.root / "licenses")
        self.assertEqual(2, code)
        self.assertIn(f"{NETCORE_LINUX} 10.0.9: no license or notices file", err)

    def test_a_folder_dotnet_publish_did_not_write_is_refused(self) -> None:
        empty = self.fx.root / "publish" / "empty"
        empty.mkdir(parents=True)
        code, _, err = self.fx.runtime_notices(empty, self.fx.root / "licenses")
        self.assertEqual(2, code)
        self.assertIn("no *.deps.json", err)

    def test_a_bundled_framework_the_deps_file_does_not_name_is_copied_all_the_same(self) -> None:
        # The deps.json form is the SDK's; should it ever change, the runtimeconfig.json still
        # says what is bundled, and a self-contained publish never passes for a
        # framework-dependent one.
        self.fx.runtime_pack(NETCORE_LINUX, "10.0.9")
        to = self.fx.root / "licenses"
        code, _, err = self.fx.runtime_notices(
            self.fx.published("orkeon", included=[("Microsoft.NETCore.App", "10.0.9")]), to)
        self.assertEqual(0, code, err)
        self.assertTrue((to / NETCORE_LINUX / "THIRD-PARTY-NOTICES.TXT").is_file())

    # --- the central pins -----------------------------------------------------------------

    def test_a_shipped_package_below_its_central_pin_fails_naming_the_apps_and_both_versions(self) -> None:
        self.fx.package("Markdig", "1.3.2", _nuspec("Markdig", "1.3.2", _expression("BSD-2-Clause")), {},
                        apps=("two",))
        self.fx.package("Iota", "10.0.9", _nuspec("Iota", "10.0.9", _expression("MIT")), {}, apps=("one", "two"))
        self.fx.save_assets()
        self.fx.write("Directory.Packages.props", _props(("Markdig", "1.4.0"), ("Iota", "10.0.12")))
        self.fx.run()
        code, _, err = self.fx.run("--check")
        self.assertEqual(1, code)
        self.assertIn("two ships Markdig 1.3.2, below the 1.4.0 Directory.Packages.props pins", err)
        self.assertIn("one, two ship Iota 10.0.9, below the 10.0.12 Directory.Packages.props pins", err)

    def test_a_version_at_or_above_its_pin_passes_and_an_unpinned_package_is_ignored(self) -> None:
        self.fx.package("Kappa", "10.0.12", _nuspec("Kappa", "10.0.12", _expression("MIT")), {})
        self.fx.save_assets()
        self.fx.write("Directory.Packages.props", _props(
            ("Alpha.Lib", "1.0"), ("Beta", "1.9.0"), ("Kappa", "10.0.9"), ("Unshipped", "9.0.0"),
            ("Gamma", "4.*"), ("Zeta.Native", "[2.0.0,3.0.0)")))
        self.fx.run()
        self.assertEqual((0, ""), self.fx.run("--check")[0::2])


if __name__ == "__main__":
    unittest.main()
