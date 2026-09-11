> 🇫🇷 [Version française](SUPPORT.fr.md)

# Getting help with Orkeon

- **Questions and discussions** — start a thread in
  [GitHub Discussions](https://github.com/Orkeon/orkeon/discussions). This is
  the right place for "how do I…", design questions, and showing what you
  built.
- **Bugs** — open an issue with the
  [bug report form](https://github.com/Orkeon/orkeon/issues/new/choose).
  `orkeon doctor` output and the install channel help a lot.
- **Feature requests** — the
  [feature request form](https://github.com/Orkeon/orkeon/issues/new/choose).
- **Security vulnerabilities** — never a public issue: follow
  [SECURITY.md](SECURITY.md) (GitHub Private Vulnerability Reporting).
- **Documentation** — start at [docs/INDEX.md](docs/INDEX.md); known
  constraints live in
  [docs/reference/limitations.md](docs/reference/limitations.md).

There is no Discord or Slack community channel at this time — Discussions is
the community venue.

## If the project stops

Nothing about Orkeon depends on its maintainer staying around:

- **The licence is MIT.** Anyone may fork, rename, relicense their fork, and
  publish it — no permission to ask, no one to reach.
- **The build is documented and reproducible from a public clone.** `git clone`
  (without `--recursive`) + `dotnet build Orkeon.sln`; the container images pin
  their base images by digest; every CI action is pinned by commit SHA; the
  release pipeline lives in `.github/workflows/` and needs nothing outside the
  repository except the publishing credentials any fork can replace with its own.
- **No private infrastructure is on the path.** The private submodules hold
  project-management material only — the build, the tests and the examples do
  not read them. There is no hosted service, no licence server, no telemetry
  endpoint that a fork would lose.
- **What you install is verifiable without trusting anyone** — see
  [Verify what you install](docs/guides/verify-what-you-install.md).

A fork that keeps the tests green is a full replacement, the day it is needed.
