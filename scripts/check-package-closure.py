#!/usr/bin/env python3
"""Package-closure gate (PUB-24 P0.2 / PUB-25).

Two guarantees, both learned the hard way (Orkeon.Application/Infrastructure
rc.1-rc.2 shipped to NuGet.org with five unrestorable Orkeon.* dependencies),
plus a source-only mode that proves as much of them as csproj files can, early:

1. RESTORABILITY — every `Orkeon.*` dependency declared by a nupkg of the
   public lineup must itself be part of the lineup. A violation means a
   consumer hits NU1101 on `dotnet restore`.

2. UMBRELLA DRIFT — the packaging projects (src/packaging/*) embed assemblies
   whose ProjectReferences carry PrivateAssets="all", so the externals of the
   embedded projects do NOT flow into the nuspec and are re-declared by hand.
   This check recomputes the union from the embedded csproj files and fails if
   the nuspec is missing any of it (a missing dependency breaks consumers at
   runtime with FileNotFoundException, not at restore).

3. SOURCE MODE (`--source-only`) — everything the csproj files alone can prove,
   with no pack and no build, so a closure regression is caught on the PR that
   introduces it instead of on the tag that ships it: the lineup ids all exist and
   are packable, no packable project has appeared outside the declared feeds, a
   lineup package would declare no out-of-lineup `Orkeon.*` nuspec dependency, and
   the umbrella externals match their embedded projects. Only the "what actually
   landed in lib/" comparison needs a real nupkg and stays tag-only.

Usage:
    # on a tag, after `dotnet pack` (publish.yml) — the full check
    python3 scripts/check-package-closure.py --artifacts artifacts \
        --lineup Orkeon Orkeon.Tools Orkeon.Rag.Onnx Orkeon.Rag.Onnx.Model \
                 Orkeon.Tools.Embeddings.Local Orkeon.Scripting.Cli

    # on every pull request, no build needed — run through scripts/check-doc-claims.py
    python3 scripts/check-package-closure.py --source-only

`--lineup` defaults to LINEUP below. publish.yml still spells the list out, and
scripts/check-doc-claims.py fails the build if any of its four hand-written copies
(this constant, the two publish.yml lists, the publication matrix + its FR mirror)
ever drifts from the others.
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PACKAGING = {
    "Orkeon": REPO / "src/packaging/Orkeon/Orkeon.csproj",
    "Orkeon.Tools": REPO / "src/packaging/Orkeon.Tools/Orkeon.Tools.csproj",
    "Orkeon.Rag.Onnx": REPO / "src/packaging/Orkeon.Rag.Onnx/Orkeon.Rag.Onnx.Package.csproj",
    "Orkeon.Tools.Embeddings.Local": REPO / "src/packaging/Orkeon.Tools.Embeddings.Local/Orkeon.Tools.Embeddings.Local.Package.csproj",
}
NON_FLOWING = {"Microsoft.CodeAnalysis.PublicApiAnalyzers"}  # PrivateAssets=all analyzers

# The canonical NuGet.org lineup (PUB-25 — docs/reference/publication-matrix.md is
# the source of truth this mirrors). Order matters for the push: `Orkeon` first,
# because every other lineup package depends on it and NuGet orders nothing.
LINEUP = [
    "Orkeon",
    "Orkeon.Tools",
    "Orkeon.Rag.Onnx",
    "Orkeon.Rag.Onnx.Model",
    "Orkeon.Tools.Embeddings.Local",
    "Orkeon.Scripting.Cli",
]

# Packable, but deliberately GitHub Packages only — never pushed to NuGet.org.
GITHUB_PACKAGES_ONLY = {"Orkeon.ConsoleApp", "Orkeon.Generators", "Orkeon.Compliance.Vfs"}


def nuspec_of(nupkg: Path) -> ET.Element:
    with zipfile.ZipFile(nupkg) as z:
        name = next(n for n in z.namelist() if n.endswith(".nuspec") and "/" not in n)
        return ET.fromstring(z.read(name))


def strip_ns(tag: str) -> str:
    return tag.split("}", 1)[-1]


def nuspec_deps(root: ET.Element) -> set[str]:
    return {
        el.attrib["id"]
        for el in root.iter()
        if strip_ns(el.tag) == "dependency"
    }


def embedded_lib_assemblies(nupkg: Path) -> set[str]:
    with zipfile.ZipFile(nupkg) as z:
        return {
            Path(n).stem
            for n in z.namelist()
            if n.startswith("lib/") and n.endswith(".dll")
        }


def csproj_refs(path: Path, kind: str) -> list[str]:
    text = path.read_text()
    return re.findall(rf'<{kind}Reference Include="([^"]+)"', text)


def embedded_projects(packaging_csproj: Path) -> list[Path]:
    text = packaging_csproj.read_text()
    out = []
    for m in re.finditer(r'<ProjectReference Include="([^"]+)"([^>]*)/?>', text):
        if 'PrivateAssets="all"' in m.group(2):
            out.append((packaging_csproj.parent / m.group(1).replace("\\", "/")).resolve())
    return out


def external_union(projects: list[Path]) -> set[str]:
    union: set[str] = set()
    for p in projects:
        union |= set(csproj_refs(p, "Package"))
    return union - NON_FLOWING


# --- source mode: csproj-only checks, runnable on a pull request -------------------------

def src_projects() -> list[Path]:
    return [
        f for f in (REPO / "src").rglob("*.csproj")
        if not any(part in ("obj", "bin", "obj-linux") for part in f.parts)
    ]


def msbuild_property(text: str, name: str) -> str | None:
    m = re.search(rf"<{name}>([^<]*)</{name}>", text)
    return m.group(1).strip() if m else None


def is_packable(text: str) -> bool:
    return (msbuild_property(text, "IsPackable") or "true").lower() != "false"


def package_id(path: Path, text: str) -> str:
    """The id this project publishes under. The two wrappers set PackageId inside a
    pack-time target (their public id collides with the wrapped project's restore
    identity), so take the last literal found anywhere in the file."""
    ids = re.findall(r"<PackageId>([^<]+)</PackageId>", text)
    return ids[-1].strip() if ids else path.stem


def project_references(text: str) -> list[tuple[str, str]]:
    return [(m.group(1), m.group(2))
            for m in re.finditer(r'<ProjectReference Include="([^"]+)"([^>]*)/?>', text)]


def source_errors(lineup: list[str]) -> list[str]:
    """Every closure guarantee the csproj files alone can prove. No pack, no build."""
    errors: list[str] = []
    text_of: dict[Path, str] = {}
    packable: dict[str, Path] = {}
    for f in src_projects():
        text = f.read_text(encoding="utf-8")
        text_of[f.resolve()] = text
        if not is_packable(text):
            continue
        pkg_id = package_id(f, text)
        if pkg_id in packable:
            errors.append(
                f"two src projects pack the same PackageId {pkg_id}: "
                f"{packable[pkg_id].relative_to(REPO)} and {f.relative_to(REPO)}"
            )
        packable[pkg_id] = f

    # 1. Every lineup id must actually be produced by a packable project.
    for pkg_id in lineup:
        if pkg_id not in packable:
            errors.append(
                f"lineup package {pkg_id} is produced by no packable project under src/ "
                f"(nothing would be pushed for it)"
            )

    # 2. No packable project outside the two declared feeds -- a new IsPackable project
    #    is a distribution decision, recorded in docs/reference/publication-matrix.md.
    stray = sorted(set(packable) - set(lineup) - GITHUB_PACKAGES_ONLY)
    if stray:
        errors.append(
            f"packable src projects outside the declared feeds: {stray} -- add them to the "
            f"lineup (or GITHUB_PACKAGES_ONLY) and to docs/reference/publication-matrix.md, "
            f"or set IsPackable=false"
        )

    # 3. Restorability, read off the ProjectReferences: a reference WITHOUT
    #    PrivateAssets="all" becomes a nuspec dependency, so its package must be in
    #    the lineup -- this is the rc.1/rc.2 NU1101 shape, one pack earlier.
    for pkg_id in lineup:
        project = packable.get(pkg_id)
        if project is None:
            continue
        text = text_of[project.resolve()]
        if (msbuild_property(text, "PackAsTool") or "false").lower() == "true":
            continue  # a tool package carries its whole publish output; no nuspec deps
        for include, attributes in project_references(text):
            if 'PrivateAssets="all"' in attributes:
                continue  # embedded assembly, not a dependency
            target = (project.parent / include.replace("\\", "/")).resolve()
            target_text = text_of.get(target)
            if target_text is None:
                errors.append(f"{pkg_id}: ProjectReference {include} resolves outside src/")
                continue
            if not is_packable(target_text):
                errors.append(
                    f"{pkg_id}: references {target.stem} without PrivateAssets=\"all\", but that "
                    f"project is IsPackable=false -- the reference yields neither a nuspec "
                    f"dependency nor an embedded assembly, so the assembly ships nowhere"
                )
                continue
            dep = package_id(target, target_text)
            if dep not in lineup:
                errors.append(
                    f"{pkg_id}: would declare a nuspec dependency on {dep}, which is outside "
                    f"the lineup (NU1101 for consumers)"
                )

    # 4. Umbrella drift, source side: the hand-declared externals of a packaging project
    #    against the union its embedded projects actually require. Same computation the
    #    artifacts mode runs against the nuspec -- the nuspec externals ARE these
    #    PackageReferences, so this half needs no nupkg.
    provided_by_orkeon: set[str] = set()
    for pkg_id, csproj in PACKAGING.items():
        declared = set(csproj_refs(csproj, "Package"))
        union = external_union(embedded_projects(csproj))
        if pkg_id == "Orkeon":
            provided_by_orkeon = declared
        else:
            union -= provided_by_orkeon  # carried transitively via the Orkeon dependency
        undeclared = sorted(union - declared)
        if undeclared:
            errors.append(
                f"{pkg_id}: external dependencies of embedded assemblies missing from "
                f"{csproj.relative_to(REPO)} (runtime FileNotFoundException for consumers): "
                f"{undeclared}"
            )
    for pkg_id in PACKAGING:
        if pkg_id not in lineup:
            errors.append(f"PACKAGING declares {pkg_id}, which is not in the lineup")

    return errors


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--artifacts",
                    help="directory of packed .nupkg files (omit with --source-only)")
    ap.add_argument("--lineup", nargs="+", default=LINEUP,
                    help="PackageIds pushed to the public feed (default: LINEUP)")
    ap.add_argument("--source-only", action="store_true",
                    help="run the csproj-only checks; no pack, no build, no artifacts")
    args = ap.parse_args()

    if args.source_only:
        errors = source_errors(list(args.lineup))
        for e in errors:
            print(f"::error::check-package-closure: {e}")
        if errors:
            return 1
        print(f"check-package-closure (source) passed - lineup {sorted(args.lineup)} "
              f"is declared, closed and free of umbrella drift")
        return 0

    if not args.artifacts:
        ap.error("--artifacts is required unless --source-only is given")
    artifacts = Path(args.artifacts)
    lineup = set(args.lineup)
    errors: list[str] = source_errors(list(args.lineup))

    nupkgs: dict[str, Path] = {}
    for f in artifacts.glob("*.nupkg"):
        pkg_id = nuspec_of(f).find(".//{*}id")
        nupkgs[pkg_id.text] = f  # type: ignore[union-attr]

    missing = lineup - nupkgs.keys()
    if missing:
        errors.append(f"lineup packages not found in {artifacts}/: {sorted(missing)}")

    # 1. Restorability of the public lineup
    for pkg_id in sorted(lineup & nupkgs.keys()):
        deps = nuspec_deps(nuspec_of(nupkgs[pkg_id]))
        orphan = {d for d in deps if d.startswith("Orkeon") and d not in lineup}
        if orphan:
            errors.append(
                f"{pkg_id}: depends on Orkeon packages outside the lineup "
                f"(NU1101 for consumers): {sorted(orphan)}"
            )

    # 2. Umbrella drift: nuspec externals vs recomputed union of embedded csproj
    provided_by_orkeon: set[str] = set()
    for pkg_id, csproj in PACKAGING.items():
        if pkg_id not in nupkgs:
            continue
        root = nuspec_of(nupkgs[pkg_id])
        deps = nuspec_deps(root)
        embedded = embedded_projects(csproj)
        expected_assemblies = {p.stem for p in embedded}
        actual_assemblies = embedded_lib_assemblies(nupkgs[pkg_id])
        if expected_assemblies != actual_assemblies:
            errors.append(
                f"{pkg_id}: embedded assemblies drifted — "
                f"missing {sorted(expected_assemblies - actual_assemblies)}, "
                f"unexpected {sorted(actual_assemblies - expected_assemblies)}"
            )
        union = external_union(embedded)
        if pkg_id == "Orkeon":
            provided_by_orkeon = nuspec_deps(root)
        else:
            union -= provided_by_orkeon  # carried transitively via the Orkeon dependency
        undeclared = {u for u in union if u not in deps}
        if undeclared:
            errors.append(
                f"{pkg_id}: external dependencies of embedded assemblies missing from the "
                f"nuspec (runtime FileNotFoundException for consumers): {sorted(undeclared)}"
            )

    if errors:
        for e in errors:
            print(f"::error::check-package-closure: {e}")
        return 1
    print(f"check-package-closure passed — lineup {sorted(lineup)} is restorable, umbrellas in sync")
    return 0


if __name__ == "__main__":
    sys.exit(main())
