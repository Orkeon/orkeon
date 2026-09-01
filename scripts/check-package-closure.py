#!/usr/bin/env python3
"""Package-closure gate (PUB-24 P0.2 / PUB-25).

Two guarantees, both learned the hard way (Orkeon.Application/Infrastructure
rc.1-rc.2 shipped to NuGet.org with five unrestorable Orkeon.* dependencies):

1. RESTORABILITY — every `Orkeon.*` dependency declared by a nupkg of the
   public lineup must itself be part of the lineup. A violation means a
   consumer hits NU1101 on `dotnet restore`.

2. UMBRELLA DRIFT — the packaging projects (src/packaging/*) embed assemblies
   whose ProjectReferences carry PrivateAssets="all", so the externals of the
   embedded projects do NOT flow into the nuspec and are re-declared by hand.
   This check recomputes the union from the embedded csproj files and fails if
   the nuspec is missing any of it (a missing dependency breaks consumers at
   runtime with FileNotFoundException, not at restore).

Usage:
    python3 scripts/check-package-closure.py --artifacts artifacts \
        --lineup Orkeon Orkeon.Tools Orkeon.Rag.Onnx Orkeon.Rag.Onnx.Model Orkeon.Tools.Embeddings.Local
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


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--artifacts", required=True)
    ap.add_argument("--lineup", nargs="+", required=True,
                    help="PackageIds pushed to the public feed")
    args = ap.parse_args()
    artifacts = Path(args.artifacts)
    lineup = set(args.lineup)
    errors: list[str] = []

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
