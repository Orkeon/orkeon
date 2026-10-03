#!/usr/bin/env python3
"""The generated package inventory of THIRD-PARTY-NOTICES.md (GAP-36, decision 6).

The `orkeon` and `orkeon-repl` dotnet tools, the `orkeon-host` service and the installers,
archives and images built from them redistribute the whole runtime closure of their
binaries: every assembly and native library a NuGet package puts in the build output. The
obligations of those packages' licenses -- MIT's notice, Apache-2.0's copy of the license and
of the `NOTICE` -- attach to every copy. This script writes the part of the notices file that
lists them, between two markers, and `--check` fails while that part lags the closure.

What is shipped: SHIPPED_APPS, the applications the installers, the tool packages and the
container images publish. A cross-check reads those recipes (the installer tables, the
`PackAsTool` projects, the Dockerfiles' `dotnet publish`) and fails on a project the list
misses: the first count of this inventory was taken on three binaries, while the installers
also ship the three Studio applications.

What is redistributed: a package of an application's restored graph (`project.assets.json`,
every target) whose target carries at least one file the build copies to the output -- a
managed or satellite assembly (`runtime`, `resource`), a native or RID-specific library
(`native`, `runtimeTargets`), a content file marked `copyToOutput`. NuGet writes an asset
group a reference excludes (`IncludeAssets`/`ExcludeAssets`) as a `_._` placeholder, which
carries nothing. Compilers and analyzers (`Microsoft.Net.Compilers.Toolset`, the
`PrivateAssets="all"` analyzers) and meta-packages (`Discord.Net`) carry no such file and are
left out; the repository's own projects are project references, never packages.

Where it reads: each package's `.nuspec` and the files beside it, in the NuGet cache the
restore filled -- the assets file's `packageFolders`, a Windows path there also tried at its
WSL mount, then `NUGET_PACKAGES`, then `~/.nuget/packages`. Never the network (CLAUDE.md: a
metered connection), so a package found in no folder is an error that says to restore.

What it writes, deterministically (sorted by id then version, no timestamp): one table row per
package version -- id, version, license (the nuspec's SPDX expression, or the file it names),
copyright, project URL, the applications that ship it, the texts it carries -- then every
license file and every notice file (`NOTICE`, third-party notices) the packages ship, verbatim
but for normalised line endings, each distinct text once. The hand-written sections above the
markers stay as they are; a package one of them covers points to it, and the version such a
section states must be the shipped one.

What else must hold, checked in both modes (GAP-45): every container image that publishes an
application copies LICENSE.md and the notices to /usr/share/doc/orkeon/ in the image it
produces, and the ignore file of its build leaves both in the context; and no shipped package
resolves below the version Directory.Packages.props pins -- central management pins direct
references only, so an application that gets a package transitively ships whatever its
dependencies ask for.

The .NET runtime a self-contained publish bundles is not a package of the closure: its runtime
packs carry their own license and third-party notices, and `--runtime-notices` copies them,
byte for byte, next to it at packaging time (scripts/package-installers.{sh,ps1}). It reads the
runtime packs the publish's *.deps.json names, finds them in the folders the inventory reads,
and copies the license and notice files of each pack's root to `<to>/<pack>/`. A
framework-dependent publish bundles none: nothing to copy.

Usage (from anywhere; the paths are the repository's):
  python3 scripts/third-party-notices.py            # rewrite the generated section
  python3 scripts/third-party-notices.py --check    # exit 1 while it is stale (CI, after restore)
  python3 scripts/third-party-notices.py --list     # print the closure, one package per line
  python3 scripts/third-party-notices.py --runtime-notices <published dir> --project <csproj> \\
      --to <payload>/licenses                       # after a `dotnet publish`

Exit codes: 0 up to date (or copied); 1 stale, or a hand-written section, a shipping recipe, an
image or a pin disagrees; 2 what this reads is missing (no assets file, a package or a runtime
pack in no folder, a runtime pack without its license and notices, no *.deps.json).
"""

from __future__ import annotations

import argparse
import difflib
import hashlib
import json
import os
import posixpath
import re
import shutil
import sys
import textwrap
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent.parent
NOTICES_FILE = "THIRD-PARTY-NOTICES.md"

# The applications this repository ships, by the name they ship under, and the project each
# one publishes. `orkeon` and `orkeon-repl` are dotnet tools (PackAsTool) and installer
# payloads, `orkeon-host` the service of the installers and of deploy/Dockerfile.host; the
# three Studio applications are added to the installers per platform by
# scripts/package-installers.{sh,ps1}. shipping_drift() keeps this list honest against those
# recipes.
SHIPPED_APPS: list[tuple[str, str]] = [
    ("orkeon", "src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj"),
    ("orkeon-repl", "src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj"),
    ("orkeon-host", "src/hosting/Orkeon.Host/Orkeon.Host.csproj"),
    ("orkeon-studio", "src/apps/Orkeon.Studio.Wpf/Orkeon.Studio.Wpf.csproj"),
    ("orkeon-studio-config", "src/apps/Orkeon.Studio.Config/Orkeon.Studio.Config.csproj"),
    ("orkeon-studio-run", "src/apps/Orkeon.Studio.Run/Orkeon.Studio.Run.csproj"),
]

# The container images. Each one ships what it publishes, so it carries the license and the
# notices too, at the Debian package's path (image_drift).
DOTNET_PUBLISH = re.compile(r"dotnet publish\s+(\S+\.csproj)")
IMAGE_RECIPES = ["Dockerfile", "Dockerfile.runners", "deploy/Dockerfile.host"]
IMAGE_DOC_DIR = "/usr/share/doc/orkeon"
IMAGE_NOTICES = ("LICENSE.md", NOTICES_FILE)

# The recipes that put a project's publish output in something this repository distributes,
# and how each one names the project.
SHIPPING_RECIPES: list[tuple[str, re.Pattern[str]]] = [
    ("scripts/package-installers.sh", re.compile(r'^\s*"[\w.-]+\|([^|"]+\.csproj)\|', re.M)),
    ("scripts/package-installers.ps1", re.compile(r"Csproj\s*=\s*'([^']+\.csproj)'")),
    *((dockerfile, DOTNET_PUBLISH) for dockerfile in IMAGE_RECIPES),
]
PACK_AS_TOOL = re.compile(r"<PackAsTool>\s*true\s*</PackAsTool>", re.I)
CENTRAL_PINS = "Directory.Packages.props"
# A pin is one version: a range or a floating version pins none, and is left out.
PLAIN_VERSION = re.compile(r"\d+(?:\.\d+){0,3}(?:-[0-9A-Za-z.-]+)?")
# How a publish's *.deps.json names a runtime pack it bundles (the SDK's DependencyContextBuilder).
RUNTIME_PACK_LIBRARY = re.compile(r"^runtimepack\.(.+)/([^/]+)$")
PRUNED_DIRS = {"bin", "obj", "obj-linux", "node_modules", "TestResults"}

BEGIN_MARKER = ("<!-- BEGIN GENERATED INVENTORY: written by scripts/third-party-notices.py, "
                "do not edit by hand -->")
END_MARKER = "<!-- END GENERATED INVENTORY -->"

REDISTRIBUTED_GROUPS = ("runtime", "runtimeTargets", "native", "resource")
LICENSE_FILE = re.compile(r"(?i)^(?:licen[cs]e|copying)(?:[._-][^/]*)?$")
NOTICE_FILE = re.compile(r"(?i)^(?:notice|third[-_ ]?party[-_ ]?notices?)(?:[._-][^/]*)?$")
# What NuGet itself writes into <licenseUrl> beside a <license> element: not a license.
GENERATED_LICENSE_URLS = ("https://aka.ms/deprecateLicenseUrl", "https://licenses.nuget.org/")
OWN_PACKAGE = re.compile(r"(?i)^Orkeon(?:\.|$)")
# A hand-written section names the package it covers: "## 6. Jint".
HAND_WRITTEN_HEADING = re.compile(r"^## (\d+)\. (\S+)", re.M)
HAND_WRITTEN_VERSION = re.compile(r"^- \*\*Version\*\*: `([^`]+)`", re.M)
# Control characters other than tab and newline: an RTF file ends with a NUL, and a NUL in
# the notices file would make git treat it as binary.
CONTROL = re.compile(r"[\x00-\x08\x0b-\x1f\x7f]")
NONE = "\u2014"
WIDTH = 90


class RestoreMissing(Exception):
    """What this script reads -- an assets file, a package, a runtime pack and its notices --
    is not on this machine."""


@dataclass
class PackageRef:
    """A package of the closure, as the assets files describe it."""
    id: str
    version: str
    path: str                 # "<id>/<version>", lowercase, under a package folder
    files: list[str]          # the package's files, as the assets file lists them
    folders: list[str]        # the packageFolders of the assets files that resolved it
    apps: list[str] = field(default_factory=list)


@dataclass(frozen=True)
class Text:
    """A license (L) or notice (N) file a package ships, normalised."""
    kind: str
    name: str
    body: str

    @property
    def key(self) -> tuple[str, str]:
        return self.kind, hashlib.sha256(self.body.encode("utf-8")).hexdigest()


@dataclass
class Package:
    id: str
    version: str
    apps: list[str]
    license: str
    copyright: str
    project: str
    texts: list[Text]


# --- the closure -----------------------------------------------------------------------------

def assets_file(project_dir: Path, platform: str = sys.platform) -> Path:
    """A project's restored assets file: obj-linux/ on Linux, obj/ elsewhere -- the split the
    root Directory.Build.props makes so a shared checkout keeps both -- else the other one."""
    names = ("obj-linux", "obj") if platform.startswith("linux") else ("obj", "obj-linux")
    for name in names:
        path = project_dir / name / "project.assets.json"
        if path.is_file():
            return path
    raise RestoreMissing(f"{project_dir.as_posix()}: no obj-linux/ or obj/ project.assets.json "
                         f"-- run `dotnet restore Orkeon.sln` first")


def _placeholder(path: str) -> bool:
    return path == "_._" or path.endswith("/_._")


def redistributed(library: dict) -> bool:
    """Whether a target library puts a file in the build output (see the module header)."""
    for group in REDISTRIBUTED_GROUPS:
        if any(not _placeholder(f) for f in library.get(group) or {}):
            return True
    return any(meta.get("copyToOutput") and not _placeholder(f)
               for f, meta in (library.get("contentFiles") or {}).items())


def closure(root: Path, apps: list[tuple[str, str]],
            platform: str = sys.platform) -> dict[tuple[str, str], PackageRef]:
    """Every third-party package the applications redistribute, keyed (id, version) lowercase,
    each with the applications that ship it in SHIPPED_APPS order."""
    refs: dict[tuple[str, str], PackageRef] = {}
    for app, csproj in apps:
        data = json.loads(assets_file((root / csproj).parent, platform).read_text(encoding="utf-8"))
        folders = list(data.get("packageFolders") or {})
        libraries = data.get("libraries") or {}
        for target in (data.get("targets") or {}).values():
            for key, library in target.items():
                if library.get("type") != "package" or not redistributed(library):
                    continue
                name, _, version = key.partition("/")
                if OWN_PACKAGE.match(name):
                    continue
                ref = refs.get((name.lower(), version.lower()))
                if ref is None:
                    meta = libraries.get(key) or {}
                    ref = PackageRef(name, version, meta.get("path") or f"{name}/{version}".lower(),
                                     list(meta.get("files") or []), [])
                    refs[(name.lower(), version.lower())] = ref
                ref.folders.extend(f for f in folders if f not in ref.folders)
                if app not in ref.apps:
                    ref.apps.append(app)
    return refs


# --- the package folders ---------------------------------------------------------------------

def _wsl_mount(folder: str) -> Path | None:
    """`C:\\Users\\me\\.nuget\\packages\\` seen from WSL: /mnt/c/Users/me/.nuget/packages."""
    match = re.match(r"^([A-Za-z]):[\\/](.*)$", folder)
    if os.name == "nt" or not match:
        return None
    return Path("/mnt", match.group(1).lower(), *[p for p in re.split(r"[\\/]", match.group(2)) if p])


def package_folders(declared: list[str], extra: list[Path], env: dict[str, str],
                    home: Path) -> list[Path]:
    """Where to look for a package, in order: the folders passed on the command line, the
    assets file's own (a Windows path also at its WSL mount; a path this OS cannot use is
    skipped, never fatal), NUGET_PACKAGES, ~/.nuget/packages. Existing directories only."""
    candidates: list[Path] = list(extra)
    for folder in declared:
        candidates.append(Path(folder))
        mount = _wsl_mount(folder)
        if mount is not None:
            candidates.append(mount)
    if env.get("NUGET_PACKAGES"):
        candidates.append(Path(env["NUGET_PACKAGES"]))
    candidates.append(home / ".nuget" / "packages")
    out: list[Path] = []
    for path in candidates:
        if path.is_absolute() and path.is_dir() and path not in out:
            out.append(path)
    return out


def locate(ref: PackageRef, folders: list[Path]) -> tuple[Path, str]:
    """The package's directory and the name of its nuspec, in the first folder that has it."""
    nuspec = next((f for f in ref.files if "/" not in f and f.lower().endswith(".nuspec")),
                  f"{ref.id.lower()}.nuspec")
    for folder in folders:
        base = folder.joinpath(*ref.path.split("/"))
        if (base / nuspec).is_file():
            return base, nuspec
    searched = ", ".join(f.as_posix() for f in folders) or "none exists"
    raise RestoreMissing(f"{ref.id} {ref.version}: not in any package folder ({searched}) "
                         f"-- run `dotnet restore Orkeon.sln` first")


# --- one package -----------------------------------------------------------------------------

def _local(tag: str) -> str:
    return tag.split("}", 1)[-1]


def _collapse(text: str | None) -> str:
    return " ".join((text or "").split())


def read_text(path: Path) -> str:
    """A license or notice file, verbatim but for its encoding (UTF-8, else Windows-1252),
    its line endings, the blank lines around it and the control characters CONTROL drops."""
    raw = path.read_bytes()
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        text = raw.decode("cp1252", errors="replace")
    text = CONTROL.sub("", text.replace("\r\n", "\n").replace("\r", "\n"))
    return re.sub(r"\A(?:[ \t]*\n)+", "", text).rstrip()


def read_package(ref: PackageRef, folders: list[Path]) -> Package:
    base, nuspec = locate(ref, folders)
    root = ET.parse(base / nuspec).getroot()
    metadata = next((e for e in root if _local(e.tag) == "metadata"), None)
    meta = {_local(e.tag): e for e in (metadata if metadata is not None else [])}

    def text(name: str) -> str:
        element = meta.get(name)
        return _collapse(element.text) if element is not None else ""

    license_files = [f for f in ref.files if "/" not in f and LICENSE_FILE.match(f)]
    license_element = meta.get("license")
    if license_element is not None and text("license"):
        if license_element.get("type") == "file":
            declared = text("license").replace("\\", "/")
            license = f"file `{declared}`"
            listed = next((f for f in ref.files if f.lower() == declared.lower()), declared)
            if listed not in license_files:
                license_files.append(listed)
        else:
            license = text("license")
    elif text("licenseUrl") and not text("licenseUrl").startswith(GENERATED_LICENSE_URLS):
        license = text("licenseUrl")
    else:
        license = "not declared"

    notice_files = [f for f in ref.files if "/" not in f and NOTICE_FILE.match(f)]

    def copy(kind: str, name: str) -> Text:
        try:
            return Text(kind, name, read_text(base.joinpath(*name.split("/"))))
        except OSError as error:
            raise RestoreMissing(f"{ref.id} {ref.version}: cannot read {name} in "
                                 f"{base.as_posix()} ({error.strerror})") from error

    texts = [copy("L", name) for name in sorted(license_files, key=lambda n: (n.lower(), n))]
    texts += [copy("N", name) for name in sorted(notice_files, key=lambda n: (n.lower(), n))]

    repository = meta.get("repository")
    project = text("projectUrl") or (_collapse(repository.get("url")) if repository is not None else "")
    return Package(id=text("id") or ref.id, version=ref.version, apps=list(ref.apps),
                   license=license, copyright=text("copyright") or NONE, project=project or NONE,
                   texts=texts)


def version_key(version: str) -> tuple:
    release, _, pre = version.split("+", 1)[0].partition("-")
    numbers = tuple(int(p) if p.isdigit() else -1 for p in release.split("."))
    return numbers, pre == "", pre


def inventory(root: Path, apps: list[tuple[str, str]], extra: list[Path], env: dict[str, str],
              home: Path, platform: str = sys.platform) -> list[Package]:
    refs = closure(root, apps, platform)
    packages = []
    # Every package of one restore names the same folders: probe them once, not 160 times
    # (a stat costs milliseconds on the NTFS mount this repository often lives on).
    probed: dict[tuple[str, ...], list[Path]] = {}
    for ref in refs.values():
        key = tuple(ref.folders)
        if key not in probed:
            probed[key] = package_folders(ref.folders, extra, env, home)
        packages.append(read_package(ref, probed[key]))
    return sorted(packages, key=lambda p: (p.id.lower(), version_key(p.version), p.version))


# --- the generated section -------------------------------------------------------------------

def hand_written_sections(document: str) -> dict[str, tuple[int, str | None]]:
    """The packages the hand-written part covers: id -> (section number, version it states)."""
    head = document.split(BEGIN_MARKER, 1)[0]
    matches = list(HAND_WRITTEN_HEADING.finditer(head))
    out: dict[str, tuple[int, str | None]] = {}
    for i, match in enumerate(matches):
        body = head[match.end():matches[i + 1].start() if i + 1 < len(matches) else len(head)]
        version = HAND_WRITTEN_VERSION.search(body)
        out[match.group(2)] = (int(match.group(1)), version.group(1) if version else None)
    return out


def _cell(value: str) -> str:
    if re.fullmatch(r"https?://\S+", value):
        return f"<{value}>"
    return value.replace("\\", "\\\\").replace("|", "\\|").replace("<", "\\<").replace(">", "\\>")


def _fence(body: str) -> str:
    longest = max((len(run) for run in re.findall(r"`+", body)), default=0)
    return "`" * max(3, longest + 1)


def _wrap(paragraph: str) -> str:
    """Generated prose, wrapped like the hand-written part (never inside a word or a URL)."""
    return textwrap.fill(" ".join(paragraph.split()), width=WIDTH,
                         break_long_words=False, break_on_hyphens=False)


def _series(names: list[str]) -> str:
    return names[0] if len(names) == 1 else f"{', '.join(names[:-1])} and {names[-1]}"


def _text_blocks(kind: str, labelled: dict[tuple[str, str], tuple[str, Text, list[Package]]]) -> list[str]:
    out: list[str] = []
    for label, text, entries in (v for k, v in labelled.items() if k[0] == kind):
        fence = _fence(text.body)
        language = "rtf" if text.name.lower().endswith(".rtf") else "text"
        shippers = [f"{p.id} {p.version}" for p in entries]
        count = "" if len(shippers) == 1 else f"{len(shippers)} packages: "
        out += [f"#### {label} \u00b7 `{text.name}`", "",
                _wrap(f"Shipped by {count}{_series(shippers)}."), "",
                "<details>", "<summary>Show the text</summary>", "",
                f"{fence}{language}", text.body, fence, "", "</details>", ""]
    return out


def render(packages: list[Package], apps: list[tuple[str, str]], document: str) -> str:
    """The generated section, both markers included, as the file must hold it."""
    labelled: dict[tuple[str, str], tuple[str, Text, list[Package]]] = {}
    counters = {"L": 0, "N": 0}
    labels: dict[int, list[str]] = {}
    for index, package in enumerate(packages):
        for text in package.texts:
            if text.key not in labelled:
                counters[text.kind] += 1
                labelled[text.key] = (f"{text.kind}{counters[text.kind]}", text, [])
            label, _, entries = labelled[text.key]
            entries.append(package)
            if label not in labels.setdefault(index, []):
                labels[index].append(label)
    hand = hand_written_sections(document)

    licenses: dict[str, int] = {}
    for package in packages:
        bucket = "a file the package ships" if package.license.startswith("file ") else package.license
        licenses[bucket] = licenses.get(bucket, 0) + 1

    out = [
        BEGIN_MARKER, "",
        "## Inventory of the redistributed packages", "",
        _wrap("Written by `scripts/third-party-notices.py`, never by hand: after a package change, "
              "run `python3 scripts/third-party-notices.py` and commit the result. CI runs it with "
              "`--check` after its restore, and fails while this section lags the restored closure."),
        "",
        _wrap(f"Every package the applications this repository ships redistribute: "
              f"{_series([f'`{name}`' for name, _ in apps])}. A package is listed when its restored "
              f"target puts at least one file in the build output: a managed or satellite assembly, "
              f"a native or RID-specific library, a content file copied to the output. Compilers, "
              f"analyzers and meta-packages put none there and are left out. {len(packages)} package "
              f"versions of {len({p.id.lower() for p in packages})} packages."),
        "",
        _wrap(f"Everything is read from each package's `.nuspec` and the files beside it, in the "
              f"NuGet cache the restore filled, never from the network. **License**: the nuspec's "
              f"SPDX expression (standard texts at <https://spdx.org/licenses/>), or the file it "
              f"names. **Copyright** and **Project**: its `<copyright>` and `<projectUrl>` (else its "
              f"`<repository>`), {NONE} when it declares none. **Texts**: the license files (`L`) and "
              f"the notices (`N`) the package ships, copied below, each distinct text once. A "
              f"package a hand-written section above also covers points to it (\u00a7)."),
        "",
        "| License | Package versions |",
        "|---|---|",
    ]
    for bucket, count in sorted(licenses.items(), key=lambda kv: (-kv[1], kv[0])):
        out.append(f"| {_cell(bucket)} | {count} |")
    out += ["",
            "| Package | Version | License | Copyright | Project | Shipped in | Texts |",
            "|---|---|---|---|---|---|---|"]
    for index, package in enumerate(packages):
        name = package.id + (f" (\u00a7{hand[package.id][0]})" if package.id in hand else "")
        license = package.license if package.license.startswith("file ") else _cell(package.license)
        out.append(f"| {_cell(name)} | {package.version} | {license} | {_cell(package.copyright)} "
                   f"| {_cell(package.project)} | {', '.join(package.apps)} "
                   f"| {', '.join(labels.get(index, [])) or NONE} |")
    out += ["",
            "### License files shipped in the packages", "",
            _wrap("Verbatim but for normalised line endings; each distinct text once, with the "
                  "packages that ship it."),
            ""]
    out += _text_blocks("L", labelled)
    out += ["### Notices shipped in the packages", "",
            _wrap("A `NOTICE` file is propagated as Apache-2.0 \u00a74(d) requires of any "
                  "redistribution; a third-party notices file lists the components a package's "
                  "binaries incorporate, and travels with them. Verbatim but for normalised line "
                  "endings; each distinct text once, with the packages that ship it."),
            ""]
    out += _text_blocks("N", labelled)
    out.append(END_MARKER)
    return "\n".join(out)


def splice(document: str, section: str) -> str:
    """The notices file with its generated section replaced, or appended when it has none."""
    begin, end = document.find(BEGIN_MARKER), document.find(END_MARKER)
    if begin < 0 and end < 0:
        return document.rstrip("\n") + "\n\n---\n\n" + section + "\n"
    if begin < 0 or end < begin:
        raise ValueError(f"{NOTICES_FILE}: the generated-section markers are incomplete or out of order")
    return document[:begin] + section + document[end + len(END_MARKER):].rstrip("\n") + "\n"


# --- the checks ------------------------------------------------------------------------------

def rows(section: str) -> dict[tuple[str, str], list[str]]:
    """The package table's rows, keyed (id, version) lowercase: the lines of seven cells."""
    out = {}
    for line in section.splitlines():
        cells = [c.strip() for c in re.split(r"(?<!\\)\|", line)[1:-1]]
        if len(cells) != 7 or cells[0] == "Package" or set(cells[0]) <= {"-"}:
            continue
        package_id = re.sub(r" \(\u00a7\d+\)$", "", cells[0])
        out[(package_id.lower(), cells[1].lower())] = cells
    return out


def staleness(document: str, section: str) -> list[str]:
    """Why the file's generated section is not `section`, package by package where it can."""
    begin, end = document.find(BEGIN_MARKER), document.find(END_MARKER)
    current = document[begin:end + len(END_MARKER)] if 0 <= begin < end else ""
    if current == section:
        return []
    have, want = rows(current), rows(section)
    problems = []
    if not current:
        problems.append(f"{NOTICES_FILE} has no generated inventory (its markers are missing)")
    missing = [want[k] for k in want if k not in have]
    gone = [have[k] for k in have if k not in want]
    changed = [want[k] for k in want if k in have and have[k] != want[k]]
    for title, entries in (("no entry for", missing), ("an entry for a package no longer shipped:", gone),
                           ("a stale entry for", changed)):
        if entries:
            problems.append(f"{len(entries)} package version(s) -- {title}")
            problems += [f"    {_row_summary(e)}" for e in entries]
    if current and not (missing or gone or changed):
        diff = difflib.unified_diff(current.splitlines(), section.splitlines(), "file", "generated",
                                    n=0, lineterm="")
        problems.append("the texts or the layout differ:")
        problems += [f"    {line}" for line in list(diff)[2:42]]
    return problems


def _row_summary(cells: list[str]) -> str:
    return f"{cells[0]} {cells[1]} ({cells[2]}; shipped in {cells[5]})"


def hand_written_drift(document: str, packages: list[Package]) -> list[str]:
    """A version a hand-written section states that the shipped closure does not have."""
    shipped: dict[str, list[str]] = {}
    for package in packages:
        shipped.setdefault(package.id, []).append(package.version)
    problems = []
    for package_id, (number, version) in sorted(hand_written_sections(document).items()):
        if package_id in shipped and version is not None and version not in shipped[package_id]:
            problems.append(f"{NOTICES_FILE} section {number} ({package_id}) states version {version}; "
                            f"the shipped closure has {', '.join(shipped[package_id])}")
    return problems


def shipping_drift(root: Path, apps: list[tuple[str, str]]) -> list[str]:
    """A project a shipping recipe publishes that SHIPPED_APPS misses, or an entry that is gone."""
    listed = {csproj for _, csproj in apps}
    problems = [f"SHIPPED_APPS lists {csproj}, which does not exist"
                for csproj in sorted(listed) if not (root / csproj).is_file()]
    shipped: dict[str, str] = {}
    for recipe, pattern in SHIPPING_RECIPES:
        path = root / recipe
        if path.is_file():
            for csproj in pattern.findall(path.read_text(encoding="utf-8")):
                shipped.setdefault(csproj.replace("\\", "/").lstrip("./"), recipe)
    for directory, subdirectories, files in os.walk(root / "src"):
        # Build output is pruned, not filtered: on a WSL mount, walking bin/ and obj/ costs
        # seconds for nothing.
        subdirectories[:] = sorted(d for d in subdirectories if d not in PRUNED_DIRS)
        for name in sorted(f for f in files if f.endswith(".csproj")):
            csproj = Path(directory, name)
            if PACK_AS_TOOL.search(csproj.read_text(encoding="utf-8")):
                shipped.setdefault(csproj.relative_to(root).as_posix(), "a PackAsTool project")
    problems += [f"{recipe} ships {csproj}, which SHIPPED_APPS in scripts/third-party-notices.py "
                 f"does not list" for csproj, recipe in sorted(shipped.items()) if csproj not in listed]
    return problems


def dockerfile_instructions(text: str) -> list[tuple[str, str]]:
    """A Dockerfile's instructions as (KEYWORD, arguments): comments and blank lines dropped,
    continuation lines joined."""
    instructions: list[tuple[str, str]] = []
    pending = ""
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if stripped.endswith("\\"):
            pending += stripped[:-1] + " "
            continue
        keyword, *arguments = (pending + stripped).split(None, 1)
        instructions.append((keyword.upper(), arguments[0] if arguments else ""))
        pending = ""
    return instructions


def image_stages(instructions: list[tuple[str, str]]) -> list[list[tuple[str, str]]]:
    """The stages the image of a plain `docker build` is made of: the last one, the stage it
    is built FROM, and so on down to a base image."""
    stages: list[tuple[str, str, list[tuple[str, str]]]] = []
    for keyword, arguments in instructions:
        if keyword == "FROM":
            words = [w for w in arguments.split() if not w.startswith("--")]
            name = words[2].lower() if len(words) > 2 and words[1].upper() == "AS" else ""
            stages.append((name, words[0].lower() if words else "", []))
        elif stages:
            stages[-1][2].append((keyword, arguments))
    chain: list[list[tuple[str, str]]] = []
    index = len(stages) - 1
    while index >= 0:
        _, base, body = stages[index]
        chain.append(body)
        index = max((i for i in range(index) if stages[i][0] and stages[i][0] == base), default=-1)
    return chain


def landed_in(stages: list[list[tuple[str, str]]], directory: str) -> set[str]:
    """The names of the files the COPY instructions of these stages put in `directory`: a
    destination ending with a slash receives each source under its own name, any other one is
    the file's own path."""
    landed: set[str] = set()
    for body in stages:
        for keyword, arguments in body:
            if keyword != "COPY":
                continue
            words = arguments.split()
            while words and words[0].startswith("--"):
                words.pop(0)
            paths = words
            if words and words[0].startswith("["):
                try:
                    paths = [str(p) for p in json.loads(" ".join(words))]
                except ValueError:
                    pass
            *sources, destination = paths or [""]
            for source in sources:
                target = PurePosixPath(destination + PurePosixPath(source).name
                                       if destination.endswith("/") else destination)
                if target.parent == PurePosixPath(directory):
                    landed.add(target.name)
    return landed


def _ignore_pattern(pattern: str) -> re.Pattern[str]:
    """A .dockerignore pattern as a regular expression: `**` any number of directories, `*`
    and `?` within one path segment, `[...]` a character class."""
    out, i = [], 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            out.append(".*")
            i += 2
        elif pattern[i] in "*?":
            out.append("[^/]*" if pattern[i] == "*" else "[^/]")
            i += 1
        elif pattern[i] == "[" and "]" in pattern[i + 1:]:
            end = pattern.index("]", i + 1)
            out.append(pattern[i:end + 1])
            i = end + 1
        else:
            out.append(re.escape(pattern[i]))
            i += 1
    return re.compile("".join(out))


def dockerignore_excludes(rules: str, path: str) -> bool:
    """Whether a .dockerignore keeps `path`, a file at the root of the build context, out of
    that context: the last pattern that matches it decides, a `!` pattern bringing it back."""
    excluded = False
    for line in rules.splitlines():
        pattern = line.strip()
        if not pattern or pattern.startswith("#"):
            continue
        negated = pattern.startswith("!")
        pattern = posixpath.normpath(pattern[1:].strip() if negated else pattern).lstrip("/")
        if _ignore_pattern(pattern).fullmatch(path):
            excluded = not negated
    return excluded


def image_drift(root: Path) -> list[str]:
    """An image recipe that publishes an application without putting LICENSE.md and the
    notices in the image it produces, or whose build context leaves them out (the ignore file
    next to the Dockerfile, else the root .dockerignore): its COPY would fail."""
    problems: list[str] = []
    kept_out: dict[str, tuple[list[str], list[str]]] = {}
    for recipe in IMAGE_RECIPES:
        path = root / recipe
        if not path.is_file():
            continue
        text = path.read_text(encoding="utf-8")
        if not DOTNET_PUBLISH.search(text):
            continue
        landed = landed_in(image_stages(dockerfile_instructions(text)), IMAGE_DOC_DIR)
        missing = [name for name in IMAGE_NOTICES if name not in landed]
        if missing:
            problems.append(f"{recipe} publishes an application, and its image does not copy "
                            f"{' and '.join(missing)} to {IMAGE_DOC_DIR}/")
        ignore = path.with_name(path.name + ".dockerignore")
        if not ignore.is_file():
            ignore = root / ".dockerignore"
        if ignore.is_file():
            rules = ignore.read_text(encoding="utf-8")
            names = [name for name in IMAGE_NOTICES if dockerignore_excludes(rules, name)]
            if names:
                kept_out.setdefault(ignore.relative_to(root).as_posix(), (names, []))[1].append(recipe)
    for ignore, (names, recipes) in kept_out.items():
        problems.append(f"{ignore} keeps {' and '.join(names)} out of the build context of "
                        f"{_series(recipes)}: add {', '.join('!' + n for n in names)} after the "
                        f"pattern that excludes them")
    return problems


def central_pins(root: Path) -> dict[str, str]:
    """The version Directory.Packages.props pins for each package, keyed by id lowercase."""
    path = root / CENTRAL_PINS
    if not path.is_file():
        return {}
    pins: dict[str, str] = {}
    for element in ET.parse(path).getroot().iter():
        package_id, version = element.get("Include"), (element.get("Version") or "").strip()
        if _local(element.tag) == "PackageVersion" and package_id and PLAIN_VERSION.fullmatch(version):
            pins[package_id.lower()] = version
    return pins


def _comparable(version: str) -> tuple:
    """version_key with the release padded to four numbers: NuGet reads 1.4 as 1.4.0.0."""
    numbers, final, pre = version_key(version)
    return numbers + (0,) * (4 - len(numbers)), final, pre


def pin_drift(root: Path, packages: list[Package]) -> list[str]:
    """A shipped package version below the one Directory.Packages.props pins."""
    pins = central_pins(root)
    problems = []
    for package in packages:
        pin = pins.get(package.id.lower())
        if pin is not None and _comparable(package.version) < _comparable(pin):
            verb = "ships" if len(package.apps) == 1 else "ship"
            problems.append(f"{', '.join(package.apps)} {verb} {package.id} {package.version}, below the "
                            f"{pin} {CENTRAL_PINS} pins -- a pin holds for direct references only: "
                            f"reference it directly in a project they build on")
    return problems


# --- the .NET runtime of a self-contained publish --------------------------------------------

def runtime_packs(publish_dir: Path) -> list[tuple[str, str]]:
    """The runtime packs a publish bundles, (pack, version): the `runtimepack` libraries of
    its *.deps.json, none for a framework-dependent publish. A framework its
    *.runtimeconfig.json says is bundled (`includedFrameworks`) and the deps.json does not
    name is `<framework>.Runtime.<rid>` all the same, so a self-contained publish never
    passes for a framework-dependent one."""
    deps = sorted(publish_dir.glob("*.deps.json"))
    if not deps:
        raise RestoreMissing(f"{publish_dir.as_posix()}: no *.deps.json -- pass the folder "
                             f"`dotnet publish -o` wrote")
    packs: dict[str, tuple[str, str]] = {}
    rid = ""
    for path in deps:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        rid = rid or ((data.get("runtimeTarget") or {}).get("name") or "").partition("/")[2]
        for key, library in (data.get("libraries") or {}).items():
            match = RUNTIME_PACK_LIBRARY.match(key)
            if match and library.get("type") == "runtimepack":
                packs.setdefault(match.group(1).lower(), (match.group(1), match.group(2)))
    for path in sorted(publish_dir.glob("*.runtimeconfig.json")):
        options = json.loads(path.read_text(encoding="utf-8-sig")).get("runtimeOptions") or {}
        for framework in options.get("includedFrameworks") or []:
            pack = f"{framework.get('name')}.Runtime.{rid}"
            packs.setdefault(pack.lower(), (pack, str(framework.get("version"))))
    return sorted(packs.values(), key=lambda p: (p[0].lower(), p[1]))


def copy_runtime_notices(publish_dir: Path, project: Path, to: Path, extra: list[Path],
                         env: dict[str, str], home: Path, platform: str = sys.platform) -> list[str]:
    """Copy, byte for byte, the license and notice files at the root of each runtime pack the
    publish bundles to `<to>/<pack>/`, from the folders the inventory reads: the project's
    restored packageFolders, NUGET_PACKAGES, ~/.nuget/packages. One line per pack copied."""
    packs = runtime_packs(publish_dir)
    if not packs:
        return []
    data = json.loads(assets_file(project.parent, platform).read_text(encoding="utf-8"))
    folders = package_folders(list(data.get("packageFolders") or {}), extra, env, home)
    found: list[tuple[str, str, Path, list[str]]] = []
    for pack, version in packs:
        base = next((folder / pack.lower() / version.lower() for folder in folders
                     if any(p.suffix.lower() == ".nuspec"
                            for p in _files(folder / pack.lower() / version.lower()))), None)
        if base is None:
            searched = ", ".join(f.as_posix() for f in folders) or "none exists"
            raise RestoreMissing(f"{pack} {version}: the runtime pack {publish_dir.as_posix()} bundles "
                                 f"is in no package folder ({searched}) -- `dotnet publish` the "
                                 f"project for that runtime identifier, or restore it with `-r`, "
                                 f"on this machine first")
        names = sorted((p.name for p in _files(base) if LICENSE_FILE.match(p.name) or NOTICE_FILE.match(p.name)),
                       key=lambda n: (n.lower(), n))
        if not names:
            raise RestoreMissing(f"{pack} {version}: no license or notices file at the root of "
                                 f"{base.as_posix()} -- a runtime does not ship without its notices")
        found.append((pack, version, base, names))
    lines = []
    for pack, version, base, names in found:
        target = to / pack
        target.mkdir(parents=True, exist_ok=True)
        for name in names:
            shutil.copyfile(base / name, target / name)
        lines.append(f"{pack} {version}: {', '.join(names)} -> {target.as_posix()}/")
    return lines


def _files(directory: Path) -> list[Path]:
    return [p for p in directory.iterdir() if p.is_file()] if directory.is_dir() else []


# --- entry point -----------------------------------------------------------------------------

def main(argv: list[str] | None = None, root: Path = ROOT,
         apps: list[tuple[str, str]] | None = None, env: dict[str, str] | None = None,
         home: Path | None = None, platform: str = sys.platform) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n", 1)[0])
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true",
                      help="exit 1 when the generated section of THIRD-PARTY-NOTICES.md is stale")
    mode.add_argument("--list", action="store_true", help="print the closure and exit")
    mode.add_argument("--runtime-notices", type=Path, metavar="PUBLISHED_DIR",
                      help="copy the license and notices of the runtime packs this publish "
                           "bundles to --to (needs --project and --to)")
    parser.add_argument("--project", type=Path, metavar="CSPROJ",
                        help="--runtime-notices: the project published, whose restore names the "
                             "package folders")
    parser.add_argument("--to", type=Path, metavar="DIR",
                        help="--runtime-notices: where each pack's files go, as <DIR>/<pack>/")
    parser.add_argument("--packages", action="append", type=Path, default=[], metavar="DIR",
                        help="a package folder to search before the assets file's own (repeatable)")
    args = parser.parse_args(argv)
    apps = SHIPPED_APPS if apps is None else apps
    env = dict(os.environ) if env is None else env
    home = Path.home() if home is None else home

    if args.runtime_notices is not None:
        if args.project is None or args.to is None:
            parser.error("--runtime-notices needs --project and --to")
        try:
            copied = copy_runtime_notices(args.runtime_notices, args.project, args.to, args.packages,
                                          env, home, platform)
        except RestoreMissing as error:
            print(f"third-party-notices: {error}", file=sys.stderr)
            return 2
        if not copied:
            print(f"third-party-notices: {args.runtime_notices.as_posix()} bundles no .NET runtime "
                  f"(a framework-dependent publish): nothing to copy.")
        for line in copied:
            print(f"third-party-notices: {line}")
        return 0

    try:
        packages = inventory(root, apps, args.packages, env, home, platform)
    except RestoreMissing as error:
        print(f"third-party-notices: {error}", file=sys.stderr)
        return 2

    if args.list:
        for p in packages:
            print(f"{p.id}\t{p.version}\t{p.license}\t{','.join(p.apps)}")
        return 0

    path = root / NOTICES_FILE
    document = path.read_text(encoding="utf-8")
    section = render(packages, apps, document)
    try:
        updated = splice(document, section)
    except ValueError as error:
        print(f"third-party-notices: {error}", file=sys.stderr)
        return 2
    problems = (hand_written_drift(updated, packages) + shipping_drift(root, apps)
                + image_drift(root) + pin_drift(root, packages))

    if args.check:
        problems = staleness(document, section) + problems
        if problems:
            print(f"third-party-notices: {NOTICES_FILE} does not cover what the shipped applications "
                  f"redistribute:", file=sys.stderr)
            for problem in problems:
                print(f"  {problem}", file=sys.stderr)
            print("  -> run `python3 scripts/third-party-notices.py` after a restore, fix what it "
                  "reports, and commit the file.", file=sys.stderr)
            return 1
        print(f"third-party-notices: {NOTICES_FILE} covers the {len(packages)} package versions "
              f"the shipped applications redistribute.")
        return 0

    if updated != document:
        path.write_text(updated, encoding="utf-8", newline="\n")
    print(f"third-party-notices: {len(packages)} package versions written to {NOTICES_FILE}.",
          flush=True)
    for problem in problems:
        print(f"third-party-notices: {problem}", file=sys.stderr)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
