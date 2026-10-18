> 🇫🇷 [Version française](../fr/adr/ADR-014-shell-confinement-by-default.md)

> **See also**: [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) · [Configuration](../reference/configuration.md) · [Known limitations](../reference/limitations.md) · [Back to the index](../INDEX.md)

# ADR-014 — Shell commands are confined by the operating system, by default

**Status**: Accepted · **Date**: 2026-10-18
· **Scope**: `src/tools/Orkeon.Tools.Code` (`ShellCommandTool`, `Confinement/`, `Sessions/`), `AgentRunScope` (`Orkeon.Domain`) and its two entry points (`AgentExecutionService`, the scripting runtime), the images, the Debian package

## Context

`shell_command` checked the paths its command line names against the VFS, and nothing else: a
script it started read and wrote wherever the host's user could, read-only mounts included, and
reached the network. Since 1.0.0-rc.4 an operator could opt into bubblewrap on Linux
(`Confinement:Mode = Bubblewrap`); nobody had to, and macOS and Windows had nothing. Each command
was also its own process, so a confined view would have been rebuilt at every call, and a
caller's cancellation left the command running.

## Decision

1. **Confinement is on by default.** `Orkeon:Tools:Shell:Confinement:Mode` gains `Auto`, the new
   default, which picks the backend of the platform:
   - **Linux**: bubblewrap (`bwrap`), each agent-facing mount bound at its virtual path;
   - **macOS**: Seatbelt (`sandbox-exec` and a generated profile), with a scratch folder of the
     session as `HOME` and `TMPDIR`;
   - **Windows 10 and later**: an AppContainer (one profile per session) inside a Job Object, the
     mounts granted to its SID by ACL. A program is looked up on `PATH` only, never in the
     working directory; a batch file (`.bat`, `.cmd`) is refused, since cmd.exe would parse its
     arguments again; a cmd.exe built-in (`echo`, `dir`, `type`) runs as
     `cmd.exe /d /v:off /s /c "<built-in> <arguments>"` inside the container, each argument that
     is not a plain word between double quotes and `%` refused.
   In every backend the process sees the agent's mounts (read-only without Write), the system
   directories read-only, nothing else of the host, and no network unless `AllowNetwork`.
2. **Off in a container.** A host in a container (`/.dockerenv`, `/run/.containerenv`,
   `DOTNET_RUNNING_IN_CONTAINER=true`) is already confined by it, and an unprivileged container
   usually refuses the user namespaces bubblewrap needs: `Auto` resolves to `None` there and logs it
   once. The images also set `Mode=None` explicitly. An explicit mode is never overridden.
3. **Fail closed.** A backend that cannot run (missing, refused by the kernel, a mode for another
   platform) fails the call with the reason and the way to turn confinement off
   (`Mode = None`). A command never falls back to running unconfined.
4. **One shell session per agent run.** The first command of an agent opens its session, bound to
   the run's `AgentRunScope`; later commands of the same run reuse it, so the confined view is
   built once and reopened only when the mounts change. On Linux and macOS the session is a
   persistent shell (`/bin/sh -s`, inside the backend) that runs each command in a subshell; on
   Windows it is the AppContainer and the Job Object, each command its own process created inside
   them. The session and every process it started are killed when the run ends, fails or is
   cancelled (the scope closes), and when the host dies: by the kernel under bubblewrap
   (`--die-with-parent` and a PID namespace) and on Windows (the Job Object, confined or not); by
   a watchdog under Seatbelt (it runs outside the sandbox, which may not signal the host's
   processes) and unconfined on Linux and macOS, which polls the host every second and kills the
   session's process tree. A timeout or a caller's cancellation kills the session; the next
   command opens a new one. On Linux and macOS a timed-out command returns no partial output.
5. **The command line stays allowlisted.** The session is a real shell, but the tool still parses
   and checks each command against its allowlist and forbidden operators before writing it to
   the session.

## Alternatives considered

- **WSL2 and bubblewrap on Windows**: one backend everywhere, but it requires WSL2 on the host,
  translates every path twice and leaves native Windows toolchains outside. Rejected.
- **A restricted token on Windows**: simpler than an AppContainer, but it does not cut the
  network and a restricted token still reads most of the disk. Rejected for AppContainer, whose
  default is to deny.
- **Windows Sandbox**: a full virtual machine per session, seconds to start and not available on
  Home editions. Rejected.
- **No confinement on macOS**: `sandbox-exec` is deprecated but still shipped and used by the
  platform's own tools; leaving macOS unconfined would make the default depend on the platform.
  Rejected.
- **An allowlist without a real shell**: what the tool did. Every command a new process, no
  state between commands, and a confined view rebuilt each time. Rejected for one confined shell
  per run, the allowlist kept in front of it.

## Consequences

- **No network by default.** A command that downloads (a package manager, `git fetch`) fails until
  `Confinement:AllowNetwork = true`.
- **Toolchains outside the system directories** (`/opt/node`, `~/.dotnet`, `nvm`) are invisible
  until their folder is listed in `Confinement:ReadOnlyPaths`.
- **Paths**: under bubblewrap the process works on virtual paths; under Seatbelt and AppContainer
  it works on physical paths, translated in and out as for an unconfined command.
- **Windows ACLs**: the AppContainer reaches a mount only through an ACE added for it; the ACEs are
  recorded in a ledger (`%LOCALAPPDATA%\Orkeon\shell-sessions\`) and removed when the session
  closes, and the first session of the next host process takes back what a crashed host left.
  Windows propagates each inheritable ACE to every object below the mount's root: a session costs
  one security-descriptor write per file of its mounts, each way. Should it prove too slow on big
  trees, the way out is a stable AppContainer SID per set of rights, granted once.
- **Windows batch files**: `npm.cmd` and every `.bat`/`.cmd` are refused in every mode.
- **The watchdog is weaker than the kernel**: under Seatbelt and unconfined on Linux and macOS, a
  process that left the session's tree (a double fork) survives a host crash.
- **Breaking change**: a host that relied on the old default (`None`) gets confinement; the opt-out
  is `Mode = None` (`CHANGELOG.md`, migration table). The Debian package depends on `bubblewrap`;
  on Ubuntu 24.04 and later AppArmor restricts unprivileged user namespaces, and an AppArmor profile
  for `/usr/bin/bwrap` that grants `userns` lifts the restriction for bubblewrap alone; without one
  the call fails with bwrap's reason.
