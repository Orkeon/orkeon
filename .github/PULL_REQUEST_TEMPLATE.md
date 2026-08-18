<!-- Thanks for contributing! The checklist mirrors what CI enforces — going
     through it before pushing saves you a red build. -->

## What & why

<!-- One or two sentences: what changes, and the problem it solves.
     Link the issue if one exists: Fixes #123 -->

## Checklist

- [ ] `dotnet build Orkeon.sln` is green (RS0016/RS0017: a new public API must be
      declared in the project's `PublicAPI.Unshipped.txt` — `dotnet format analyzers
      --diagnostics RS0016` does it for you; see CONTRIBUTING → Versioning).
- [ ] Tests added/updated for behavior changes, and the affected test projects pass.
- [ ] Docs updated when behavior or configuration changes — **including the French
      mirror** (`docs/fr/**`, `*.fr.md`): the parity check in CI fails on a missing
      mirror (`bash scripts/check-docs-parity.sh` locally).
- [ ] `CHANGELOG.md` entry under `[Unreleased]` for anything user-visible.
- [ ] New shell scripts (or shebang scripts) committed with the executable bit
      (`git update-index --chmod=+x <script>`).
- [ ] No direct `System.IO` in framework code — route through `IFileSystemService`
      (the VFS analyzer enforces this).

## Notes for the reviewer

<!-- Anything non-obvious: trade-offs, follow-ups deliberately left out, areas
     you want a careful look at. -->
