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
   - **macOS**: Seatbelt (`sandbox-exec` and a generated profile);
   - **Windows**: an AppContainer inside a Job Object, the mounts granted to it by ACL.
   In every backend the process sees the agent's mounts (read-only without Write), the system
   directories read-only, nothing else of the host, and no network unless `AllowNetwork`.
2. **Off in a container.** A host in a container (`/.dockerenv`, `/run/.containerenv`,
   `DOTNET_RUNNING_IN_CONTAINER=true`) is already confined by it, and an unprivileged container
   usually refuses the user namespaces bubblewrap needs: `Auto` resolves to `None` there and logs it
   once. The images also set `Mode=None` explicitly. An explicit mode is never overridden.
3. **Fail closed.** A backend that cannot run (missing, refused by the kernel, a mode for another
   platform) fails the call with the reason and the way to turn confinement off
   (`Mode = None`). A command never falls back to running unconfined.
4. **One shell session per agent run.** The first command of an agent opens a persistent shell
   (`/bin/sh -s`, inside the backend), bound to the run's `AgentRunScope`; later commands of the
   same run reuse it, so the confined view is built once and reopened only when the mounts change.
   The session and every process it started are killed when the run ends, fails or is cancelled
   (the scope closes), and when the host dies (`--die-with-parent` and a PID namespace under
   bubblewrap, a watchdog unconfined, the Job Object on Windows). A caller's cancellation kills the
   running command.
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
  recorded and removed when the session closes, and leftovers of a crashed host are cleaned at the
  next start.
- **Breaking change**: a host that relied on the old default (`None`) gets confinement; the opt-out
  is `Mode = None` (`CHANGELOG.md`, migration table). The Debian package depends on `bubblewrap`;
  on Ubuntu 24.04 and later the AppArmor profile for `/usr/bin/bwrap` lifts the restriction on
  unprivileged user namespaces for bubblewrap alone.
