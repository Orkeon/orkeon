> 🇫🇷 [Version française](../fr/architecture/service-host.md)

# The service host and the chat gateway

**Scope**: `orkeon-host` — the daemon that hosts crews, and the gateway that lets people reach them from a chat channel.
**Audience**: whoever installs and operates Orkeon on a server.

Until this, Orkeon could be run from a terminal or embedded in a program. Both assume a human in front of a screen, on the same machine, for the length of one process. The service host lifts all three assumptions; the Discord channel gives the first place people can talk to it from somewhere they already are.

---

## 1. What it is, and what it is not

**It is** a long-lived process that hosts one or more crews, isolates each run, bounds concurrency, answers a chat channel, and stops without abandoning work in flight.

**It is not a scheduler.** rc.2 ships none, deliberately. A crew that should run every morning still needs the artifact `orkeon forge promote --schedule` produces — a Windows task, a systemd timer or a cron line — installed by a person. The host lays the foundation for one; it does not pretend to be it, and no part of this document should be read as saying otherwise.

The same binary runs three ways: in a terminal, as a systemd unit, as a Windows service. `UseSystemd()` and `UseWindowsService()` are inert outside their supervisor, so nothing is built differently. A daemon you cannot run in the foreground is a daemon you cannot debug.

---

## 2. Configuring it

```json
{
  "Llm": { "Provider": "deepseek", "Model": "deepseek-chat" },

  "Orkeon": {
    "Host": {
      "RunTimeout": "00:30:00",
      "ShutdownGracePeriod": "00:00:20",

      "Crews": [
        {
          "Name": "support",
          "Path": "/srv/orkeon/crews/support",
          "Mounts": [ "/srv/orkeon/out/support:/output:rw" ],
          "Profile": {
            "MaxConcurrentRuns": 4
          }
        },
        {
          "Name": "veille",
          "Path": "/srv/orkeon/crews/veille",
          "Mounts": [ "/srv/orkeon/out/veille:/output:rw" ]
        }
      ],

      "Discord": {
        "Enabled": true,
        "TokenEnvironmentVariable": "ORKEON_DISCORD_TOKEN",
        "AllowedUserIds": ["123456789012345678"],
        "ProgressInterval": "00:00:02",
        "GuildIds": []
      }
    }
  }
}
```

### `Mounts` — a mount namespace per hosted crew

The two crews above both address `/output`, over two different folders. That is the point of the
key: a team's mounts are its own namespace, not an entry in a shared table.

The host **grants** those folders; the crew does not declare them. `CrewRunner` enters an ambient
`IFileSystemScope` for the run, over a registry composed by `ScopedMountComposition.ForExecution`,
so two crews running at the same time never see each other's mounts. A crew with no `Mounts` keeps
the boot mounts, unchanged.

Two things worth knowing before you use it. Entering a scope **replaces** the mount set rather
than merging with it, so the composed registry carries the boot Internal mounts (`/llm-logs`,
`/sandbox`) forward — without that, exchange logging and the code sandboxes would fail for the
length of the run, and the check that stops either gaining a second, agent-reachable address would
go with them. And `IPathValidator` is a second, process-wide gate whose allowed roots are captured
at boot: a granted folder outside the workspace root resolves in the namespace and is then refused
there, unless you widen `PathSecurity:AdditionalAllowedDirectories`.

`Path` accepts what `orkeon run` accepts: a YAML file, a multi-file crew directory, or an `.ork.ts` script. The host loads it through the same code path, so a hosted crew is exactly the crew a terminal launches. Each crew's directory is **mounted read-only into the VFS automatically**, under a name — `/crews`, then `/crews-1`, `/crews-2`, … for each further directory — and the crew is loaded by that virtual spelling (`/crews/support.yaml` for a file, `/crews-1` for a directory). The loader reads through the virtual file system like everything else in the framework, and a path that only existed on the physical disk would pass the startup probe and then fail on every message. The mount is deliberately *not* identity-mapped: an agent that calls `list_mounts`, or reads any access-denied message, must never be handed the operator's disk layout ([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)). A `--mount` of your own claiming `/crews*` is refused at start with exit code 78. One caveat travels with the script form: transpiling `.ork.ts` needs esbuild on the machine, and neither the container image nor a bare service install carries it — a daemon-hosted crew is a YAML crew unless you install esbuild yourself.

The configuration is **validated at start**: a missing crew path, a zero timeout, an enabled channel with an empty allow list or an unset token variable all refuse the start with exit code 78 — before the service ever reports ready — rather than being discovered one failed run at a time.

### No secret is ever written here

`TokenEnvironmentVariable` holds the **name** of an environment variable. The token itself never touches the configuration file, a commit, or a container image layer — where it would remain for as long as the image exists, including after someone "removes" it in a later layer. This is the rule the LLM providers already follow, and a bot token, which can read every message a server sends, does not get an exception.

### The profile: one axis, on purpose

| Axis | Default | Why |
|---|---|---|
| `MaxConcurrentRuns` | `4` | A daemon accepting every request that arrives dies under its first burst, and a chat channel makes bursts trivial. |

The profile deliberately carries nothing else. Earlier drafts sketched `Interactive`,
`Persistent` and `Chat` flags; the review found them bound from configuration and read by
nothing — an operator could flip them and change nothing at all. Configuration surface that
does nothing is worse than absent, so rc.2 ships the one knob that works.

A request beyond the ceiling is **refused with an answer**, not queued: "we are busy, try again shortly" is something a channel relays to a person; an invisible queue is not.

---

## 3. Isolation

Each run gets its own dependency-injection scope, and each run's per-crew state is released when it ends. Between them they carry the defence against the risk the gateway design calls its most serious: state leaking between conversations.

Two mechanisms, stated precisely because an earlier version of this section overstated what stood behind them. The **scope** is what isolates the scoped services — the crew repository above all, so one conversation's crew is never resolvable from another's run. The **release** is what keeps the process-wide services honest: every message loads a fresh crew with a fresh id, and the memory service and provider registry drop their entry for it when the run ends — otherwise a daemon accumulates one per conversation, forever. Memory outliving a hosted run is impossible by construction in rc.2 — there is no flag to get wrong.

Each run also carries its own deadline (`RunTimeout`). A daemon has nobody watching to press Ctrl-C, so a run with no timeout is a stuck daemon waiting on a model that will never answer.

---

## 4. The gateway

A message becomes a run in a fixed order: **authorize, route, acknowledge, work.**

**Authorize first.** A sender who is not on the allow list never reaches a crew, never costs a token, and never appears in a log as an accepted request. They are told, because silence looks like a broken bot.

> **An empty allow list denies everyone**, and the channel refuses to start rather than answering nobody in silence. The opposite default is how a bot invited to a public server ends up spending someone's API budget on strangers.

**Route.** rc.2 ships one strategy: **a thread is a run**. It is the only mapping a person can predict without being told — what happens in this thread is one job — and it gives parallelism without inventing a notion of session anyone has to learn. A second message in a running thread is refused with an explanation rather than starting a second run whose answers nobody could tell apart.

**Acknowledge.** Every chat platform's response window is measured in seconds; a crew is measured in minutes. The acknowledgement rides on **admission**: it goes out the moment the run's slot is reserved — still before any crew work, and carrying the stop button, so a run can be interrupted from its first second. A refusal (unknown crew, busy) is answered without an acknowledgement: "working on it" plus a Stop button, followed by "we are busy", would be a promise retracted by its own next line — with a button attached to nothing.

**Work**, reporting as it goes. Progress is **throttled** (`ProgressInterval`, 2 seconds by default): a run emits an event per agent thought and per tool call, and relaying each one would exhaust Discord's per-channel rate limit inside a single crew. The last suppressed update is flushed just before the final answer, so a run does not end on a view several steps stale.

### Commands

`/status` and `/stop` are **registered slash commands** — Discord's client autocompletes them, and the reply is **ephemeral**: a status poke or a refusal is the invoker's business, not one more line in everyone's thread. They are registered at connect time: globally when `GuildIds` is empty (no configuration, but Discord caches global commands for up to an hour), or per named guild (immediately available — the dev loop). The gateway does not parse message text for them: a literal `/stop` typed as plain text is a prompt like any other.

| Command | Effect |
|---|---|
| `/status` | What this conversation is running, and since when. |
| `/stop` | Stops this conversation's run. The **Stop button** is the same invocation with a different finger — same allow-list check, same wording back. |

Both paths are gated by `AllowedUserIds`. That includes the button: a click from someone off the list is refused ephemerally instead of stopping the run.

---

## 5. Installing it

### systemd

[`deploy/systemd/orkeon-host.service`](https://github.com/orkeon/orkeon/blob/main/deploy/systemd/orkeon-host.service).

```bash
sudo cp deploy/systemd/orkeon-host.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now orkeon-host
journalctl -u orkeon-host -f
```

`Type=notify`, because the host reports readiness once its configuration has been accepted — every crew path probed, every option validated — rather than when the process starts. Otherwise systemd would call a host that failed to read its configuration "started" for as long as it took to exit.

`Restart=on-failure`, not `always`, and `RestartPreventExitStatus=78`: a configuration the host refuses — no crew, a path that does not exist, an empty allow list, an unset token variable — exits 78 (EX_CONFIG) *before readiness*, and restarting on it every ten seconds would bury the one message the operator needs to read. A crash exits non-zero and does restart; a channel that dies takes the host down with exit 1 for the same reason — a daemon that exists to be reachable must not survive its own deafness with a clean status.

There is deliberately **no `WatchdogSec`**: the .NET systemd integration sends `READY=1` and `STOPPING=1` and no watchdog keepalive, so arming one would make systemd kill a healthy host on its first missed — never-sent — ping.

`TimeoutStopSec` is deliberately longer than `ShutdownGracePeriod`, so runs in flight get their grace before systemd loses patience. **Raise one without the other and the one left behind stops meaning anything.**

Secrets go in `/etc/orkeon/orkeon-host.env`, readable only by the service user. The unit file stays free of them.

### Windows

The **full** archive (`orkeon-<version>-win-x64.zip` — not the CLI zip) carries
the daemon and its deployment assets. The real executable is
`libexec\orkeon-host\orkeon-host.exe`; `bin\orkeon-host.cmd` is a terminal
wrapper — never register the wrapper with the SCM. The registration script
ships in the archive's `deploy\windows\` folder:

```powershell
.\deploy\windows\install-service.ps1 `
  -ExecutablePath 'C:\Program Files\Orkeon\libexec\orkeon-host\orkeon-host.exe' `
  -SettingsPath   'C:\ProgramData\Orkeon\appsettings.json'
Start-Service -Name Orkeon
```

Use **absolute paths** everywhere — in both parameters and inside the settings
file: a Windows service starts in `System32`, and the host resolves its
configuration against the current directory.

Recovery mirrors the systemd policy as closely as the SCM allows: the script
arms restart-on-crash twice, then stop. The SCM cannot filter exit codes, so
there is no equivalent of `RestartPreventExitStatus=78` — a refused
configuration shows up as a stopped service, not a restart loop. Secrets stay
environment variables named by the configuration — set them in the machine or
service-account environment, never on the registration command line. The service
runs as LocalSystem today, and error messages go to stderr, which the SCM does
not surface: to read a configuration error, run the executable in a terminal
with the same arguments.

### Container

[`deploy/Dockerfile.host`](https://github.com/orkeon/orkeon/blob/main/deploy/Dockerfile.host). The token is passed by name at run time, never baked into a layer.

---

## 6. What ships, and what does not

**Ships**: the host and its lifetime, the crew registry with per-run isolation and a concurrency ceiling, the gateway ports, the allow-list authorizer, thread-is-run routing, the throttled responder, and the Discord channel with registered `/status` and `/stop` slash commands and the stop button — one authorized path for all three.

**Does not ship**, and is not implied anywhere: a scheduler, hot configuration reload, multi-crew dynamic hosting, and every channel other than Discord. The gateway ports are shaped so the run event bus's JSONL protocol is a legitimate implementation of the same contract — the model is not closed around chat — but that channel is not written.

**One thing cannot be verified in CI**: the specification's own acceptance criterion — launching a crew from a real Discord thread, watching it progress, stopping it by button, with the service running as a systemd daemon. It needs a Discord account and a server, which is an owner action. What CI does hold is everything either side of the socket: the message translation, the two platform limits, authorization, routing, throttling and isolation.

---

## 7. See also

- [The run event bus](run-event-bus.md) — the protocol a watching process reads, and the shape the gateway's ports were written to accommodate.
- [EventHub and crew lifecycle](event-hub-and-crew-lifecycle.md) — inter-crew messaging and its ACL.
- [Publication matrix](../reference/publication-matrix.md) — where `orkeon-host` ships.
