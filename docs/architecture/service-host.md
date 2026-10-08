> 🇫🇷 [Version française](../fr/architecture/service-host.md)

# The service host and the chat gateway

**Scope**: `orkeon-host` — the daemon that hosts crews, the gateway that lets people reach them from a chat channel, and the A2A server that lets other agents run them.
**Audience**: whoever installs and operates Orkeon on a server.

Until this, Orkeon could be run from a terminal or embedded in a program. Both assume a human in front of a screen, on the same machine, for the length of one process. The service host lifts all three assumptions; the Discord channel gives the first place people can talk to it from somewhere they already are.

---

## 1. What it is, and what it is not

**It is** a long-lived process that hosts one or more crews, isolates each run, bounds concurrency, answers a chat channel and — when the configuration exposes crews — A2A peers, and stops without abandoning work in flight.

**It is not a scheduler.** Orkeon ships none, deliberately. A crew that should run every morning is run by the operating system, from the artifact `orkeon forge promote --schedule` produces — a Windows task, a systemd timer or a cron line — which `orkeon forge schedule` installs (Orkeon Studio asks the user first) and `orkeon forge unschedule` removes. The host lays the foundation for one; it does not pretend to be it, and no part of this document should be read as saying otherwise.

The same binary runs three ways: in a terminal, as a systemd unit, as a Windows service. `UseSystemd()` and `UseWindowsService()` are inert outside their supervisor, so nothing is built differently. A daemon you cannot run in the foreground is a daemon you cannot debug.

---

## 2. Configuring it

```json
{
  "Llm": { "BaseUrl": "https://api.deepseek.com", "Model": "deepseek-chat" },

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
          "Description": "Weekly technology watch: what changed, with sources.",
          "Mounts": [ "/srv/orkeon/out/veille:/output:rw" ]
        }
      ],

      "A2A": {
        "Enabled": true,
        "Port": 5002,
        "Crews": [ "veille" ]
      },

      "Discord": {
        "Enabled": true,
        "TokenEnvironmentVariable": "ORKEON_DISCORD_TOKEN",
        "AllowedUserIds": ["123456789012345678"],
        "ProgressInterval": "00:00:02",
        "GuildIds": [],
        "DefaultCrew": "support",
        "Routes": { "234567890123456789": "veille" }
      }
    }
  }
}
```

The model's key is not in the file either: it comes from `ORKEON_Llm__ApiKey`, set in the service's environment (see *Installing it*), or from the variable the file names instead (`Llm:ApiKeyEnvVar`, and `Llm:Profiles:<name>:ApiKeyEnvVar` per profile — see [configuration](../reference/configuration.md#the-api-key-apikey-apikeyenvvar)), set in that same environment. A host reads the variables of the account it runs as — on Windows its user scope too, never copied into the process: a key Orkeon Studio remembered for you reaches an `orkeon-host` you start yourself, not one running under a service account. Every key can be overridden the same way — the `ORKEON_` prefix, `__` for `:` — so `ORKEON_Orkeon__Host__RunTimeout=00:10:00` shortens the deadline without touching the file.

**Which providers the hosted crews may use.** A host can offer several LLM providers as named profiles (`Llm:Profiles:<name>`, see [Configuration](../reference/configuration.md#named-profiles-llmprofiles)), and a crew picks one per agent by name. The daemon runs crews it does not control, so it decides which names answer: `Orkeon:Host:LlmProfiles` is an **allow-list**. Unset, every profile the configuration defines is offered; set, only those listed — a crew naming any other one fails to load, and the thread that started it says why. The default profile (the `Llm` section) is always offered, so `"LlmProfiles": ["default"]` keeps every hosted crew on it. The list holds for every role a crew gives a profile — its agents, its tasks, its hierarchical manager (through the manager agent's `llm:` block) — and for the RAG subsystem's `Orkeon:Rag:LlmProfile` too. An entry naming a profile `Llm:Profiles` does not define, or a RAG profile the list leaves out, refuses the start. The startup lines follow the list: the host names the profiles it offers (`LLM profiles offered to crews besides the default: …`), each with where its key comes from, and those it hides on a line of their own (`LLM profiles hidden from crews by the host's allow-list: …`) — so the list is seen at work —, and it warns about a key reference that resolves nothing, on its log and on stderr, for the default and the offered profiles only: a key missing on a profile no crew can name is no reason to warn. Those lines are Information (see the log level below); without a list, every profile is offered and named.

```json
{
  "Llm": {
    "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-v4-flash",
    "Profiles": { "claude": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5" } }
  },
  "Orkeon": { "Host": { "LlmProfiles": [ "default" ] } }
}
```

### The command line

```
orkeon-host [--settings <file>] [--working-dir <dir>] [--mount <physical>:<virtual>:<ro|rw|rwnd>]... [--allow-external-mounts]
```

| Flag | Effect |
|---|---|
| `-s`, `--settings <file>` | The configuration file. Defaults to `./appsettings.json`, resolved against the working directory; a file named here replaces it — the two are never laid one over the other — and one that does not exist refuses the start. |
| `--working-dir <dir>` | Moves there before anything is read, so relative settings and crew paths resolve against it. |
| `-m`, `--mount <spec>` | An extra VFS mount, repeatable. The crew directories are mounted without it (below). |
| `--allow-external-mounts` | Allows mounts outside the working directory. Implied as soon as one crew is configured, since each crew directory is itself mounted from wherever it lives. |
| `-h`, `--help` / `--version` | Print and exit `0`. |

There are no subcommands. The exit codes are the contract with the supervisor: `0` for a clean stop, `78` (EX_CONFIG) for a configuration refused at start, `1` when the chat channel died.

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

The configuration is **validated at start**: no crew under `Orkeon:Host:Crews`, a crew without a `Name` or a `Path`, two crews with one name (compared case-insensitively), a crew path that does not exist, a `MaxConcurrentRuns` below 1, a `RunTimeout` that is zero or negative, a negative `ShutdownGracePeriod`, an `LlmProfiles` entry naming a profile `Llm:Profiles` does not define, and — for an enabled channel — an empty allow list, an unset token variable, a `ProgressInterval` that is zero or negative, a `GuildIds` entry or `Routes` key that is not a number, or a `Routes` entry or `DefaultCrew` naming a crew the host does not declare; for an enabled A2A server, no exposed crew, an exposed crew the host does not declare, a malformed `Host` or `Port`, or a listener beyond the loopback interface with no authentication, and — whatever the A2A switch — an `A2A:EnableServer`, `A2A:Host` or `A2A:Port` key, which the daemon does not read (see [Other agents (A2A)](#5-other-agents-a2a)), and a listener the system refuses — a URL HTTP.sys reserved for no one, a port another process holds (see [Windows](#windows)); and, before any of them, a setting the runner host refuses — a value the configuration binder cannot convert (`"RunTimeout": "abc"`, `"Orkeon:Guardian:Enabled": "oui"`) or that a rule of its section refuses, a key no section carries (`Orkeon:Host:RunTimeoutt`, or one that is no setting any more, `RaggableTree:Exclude`), a section or a component's name it does not know (`Orkeon:Guardain`, a memory provider, a RAG reranker), a log level that is none, an LLM profile it cannot build, a settings file it cannot read, an address that is no address (`RaggableTree:Embedding:BaseUrl`, `Telemetry:OtlpEndpoint`), all refuse the start with exit code 78 — before the service ever reports ready — rather than being discovered one failed run at a time. Every setting the daemon reads is judged then, whether a run uses it or not; the e-mail accounts are the one exception — an account that cannot be read is set aside and reported when a run names it ([when a setting is refused](../reference/configuration.md#when-a-setting-is-refused)).

The whole sequence that precedes the start runs under that barrier: the configuration the daemon boots from, its own sections — `Orkeon:Host` with its `A2A`, `Orkeon:Host:Discord`, and the `A2A` section —, **read once, at start**, and the runner host itself. Each refusal is one line naming the key — or the file, with the line and the position of what its JSON gets wrong — on stderr and, under the Windows SCM, in the Application event log. A value the binder cannot convert used to surface later, as a crash when the host built its services; a settings typo used to crash the daemon outright, and systemd restarted it every ten seconds.

Crews are read **once**, at start: there is no directory scan and no reload. Adding a crew is an edit of the file and a restart.

### No secret is ever written here

`TokenEnvironmentVariable` holds the **name** of an environment variable. The token itself never touches the configuration file, a commit, or a container image layer — where it would remain for as long as the image exists, including after someone "removes" it in a later layer. This is the rule the LLM providers already follow, and a bot token, which can read every message a server sends, does not get an exception.

### The profile: one axis, on purpose

| Axis | Default | Why |
|---|---|---|
| `MaxConcurrentRuns` | `4` | A daemon accepting every request that arrives dies under its first burst, and a chat channel makes bursts trivial. |

The profile deliberately carries nothing else. Earlier drafts sketched `Interactive`,
`Persistent` and `Chat` flags; the review found them bound from configuration and read by
nothing — an operator could flip them and change nothing at all. Configuration surface that
does nothing is worse than absent, so the host ships the one knob that works.

A request beyond the ceiling is **refused with an answer**, not queued: "we are busy, try again shortly" is something a channel relays to a person; an invisible queue is not. The ceiling is the crew's, whichever way the runs came in: chat conversations and A2A tasks share it.

---

## 3. Isolation

Each run gets its own dependency-injection scope, and each run's per-crew state is released when it ends. Between them they carry the defence against the risk the gateway design calls its most serious: state leaking between conversations.

Two mechanisms, stated precisely because an earlier version of this section overstated what stood behind them. The **scope** is what isolates the scoped services — the crew repository above all, so one conversation's crew is never resolvable from another's run. The **release** is what keeps the process-wide services honest: every message loads a fresh crew with a fresh id, and the memory service and provider registry drop their entry for it when the run ends — otherwise a daemon accumulates one per conversation, forever. Memory outliving a hosted run is impossible by construction — there is no flag to get wrong.

Each run also carries its own deadline (`RunTimeout`). A daemon has nobody watching to press Ctrl-C, so a run with no timeout is a stuck daemon waiting on a model that will never answer.

---

## 4. The gateway

A message becomes a run in a fixed order: **authorize, route, acknowledge, work.**

**Authorize first.** A sender who is not on the allow list never reaches a crew, never costs a token, and never appears in a log as an accepted request. They are told, because silence looks like a broken bot.

> **An empty allow list denies everyone**, and the channel refuses to start rather than answering nobody in silence. The opposite default is how a bot invited to a public server ends up spending someone's API budget on strangers.

**Route.** The host ships one strategy: **a thread is a run**, and **the room picks the crew** (GAP-11). A thread opened in a Discord channel listed under `Discord:Routes` (channel id → crew name) starts that crew; a thread anywhere else starts `Discord:DefaultCrew` — the first crew of `Orkeon:Host:Crews` when it is unset. In the example above, threads of the channel `234567890123456789` reach `veille`, and every other channel's reach `support`. A hosted crew no room reaches is named by a warning at startup. Channel ids are what Discord shows with Developer Mode on (right-click the channel → Copy Channel ID); a thread inside a forum channel follows the forum's route. It is the mapping a person can predict without being told — `#billing` answers billing, and what happens in this thread is one job — and it gives parallelism without inventing a notion of session anyone has to learn. A second message in a running thread is refused with an explanation rather than starting a second run whose answers nobody could tell apart.

**Acknowledge.** Every chat platform's response window is measured in seconds; a crew is measured in minutes. The acknowledgement rides on **admission**: it goes out the moment the run's slot is reserved — still before any crew work, and carrying the stop button, so a run can be interrupted from its first second. A refusal (unknown crew, busy) is answered without an acknowledgement: "working on it" plus a Stop button, followed by "we are busy", would be a promise retracted by its own next line — with a button attached to nothing.

**Work**, reporting as it goes. The final answer is posted in the thread, cut to Discord's 2,000 characters (with a `…(truncated)` marker); an empty answer reads `(no output)`. Progress is **throttled** (`ProgressInterval`, 2 seconds by default): a run emits an event per agent thought and per tool call, and relaying each one would exhaust Discord's per-channel rate limit inside a single crew. The last suppressed update is flushed just before the final answer, so a run does not end on a view several steps stale.

### What the Discord channel listens to

Messages **inside a thread**, from people: a message posted in a plain channel is ignored, and so is every message from a bot. The client connects with the `Guilds`, `GuildMessages` and `MessageContent` intents — `MessageContent` is a privileged intent, to be switched on for the bot in the Discord developer portal, or every message arrives empty. `AllowedUserIds` is the only access control: `GuildIds` chooses where the slash commands are registered, it does not restrict who may talk to the bot.

### Commands

`/status` and `/stop` are **registered slash commands** — Discord's client autocompletes them, and the reply is **ephemeral**: a status poke or a refusal is the invoker's business, not one more line in everyone's thread. They are registered at connect time: globally when `GuildIds` is empty (no configuration, but Discord caches global commands for up to an hour), or per named guild (immediately available — the dev loop). The gateway does not parse message text for them: a literal `/stop` typed as plain text is a prompt like any other.

| Command | Effect |
|---|---|
| `/status` | What this conversation is running, and since when. |
| `/stop` | Stops this conversation's run. The **Stop button** is the same invocation with a different finger — same allow-list check, same wording back. |

Both paths are gated by `AllowedUserIds`. That includes the button: a click from someone off the list is refused ephemerally instead of stopping the run.

---

## 5. Other agents (A2A)

The chat channel is one way in; [A2A](../reference/a2a-conformance.md) is the other. Turned on, the host serves an agent card and takes tasks from other agents — another Orkeon process, or any client of the same REST dialect — and **each crew it exposes is one skill**. A task is a **run of that crew**, exactly what a chat message is: the task's `input` is the run's need, and it goes through the same runner — under the crew's mounts, inside its `MaxConcurrentRuns`, under `RunTimeout`, logged with its origin (`a2a:<task id>`), drained on stop.

| `Orkeon:Host:A2A` key | Default | Effect |
|---|---|---|
| `Enabled` | `false` | Serves the exposed crews over A2A. Off, the host listens on no port. |
| `Host`, `Port` | `http://localhost`, `5002` | The listener. `http://+` listens on every interface. |
| `Crews` | none | The crews other agents may run, by name. Exposing is a choice per crew, like a chat route: a crew left out is invisible — absent from the card, and a task naming it fails like any unknown skill. |

**What a peer sees.** `GET /.well-known/agent.json` lists one skill per exposed crew: its `id` and `name` are the crew's `Name` as `Orkeon:Host:Crews` declares it, its `description` the crew's `Description` key when the configuration gives one. The card comes from the configuration, not from loaded crews: the daemon loads a fresh crew for every run and keeps none in between, so there is no agent directory to read — and none is kept. The card's own identity is read from `A2A` as for any Orkeon A2A server (`AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl`).

**What a task does.** `POST /a2a/tasks/send` (or `sendSubscribe`) whose `skillId` is a published id — exactly, case included — runs that crew on `input`; the request's `metadata` become the run's variables. The answer:

- the run finishes → `Completed`, `output` is the crew's answer;
- the run fails → `Failed`, `error` is the sentence a chat thread gets — the run id to look up in the host log, never the detail (paths, endpoints) the log keeps;
- the crew is at its `MaxConcurrentRuns` — chat conversations and A2A tasks counted together → `Failed`, saying so; a task is never queued;
- `DELETE /a2a/tasks/{id}` stops the run, and the task answers `Cancelled`; a run past `RunTimeout` answers `Cancelled` too, saying it timed out;
- the host is stopping → `Failed`, `error` saying "The host is stopping: this run was not started. Send it again once the host is back." — nothing was loaded, no model was called;
- a `skillId` the card does not publish → `Failed`, naming the published skills; an empty `input` → `Failed` too.

**Following a task.** `sendSubscribe` streams the run as server-sent events: `Working`, then one `Working` update per line a chat thread reads — `Running '<crew>'…`, then `✔ <role> — step N done` for each finished task — the line in its `message` (`partialOutput` stays the output), then the final state and `[DONE]`. Nothing follows the final state; a peer that leaves mid-run reads no more of it, and the run goes on to its end.

`GET /a2a/tasks/{id}` answers `501`: the daemon keeps no task records.

**Security.** The `A2A:Security` section of the same file applies as written — `ApiKey` (the accepted keys named by `ApiKeySecretNames`, read from `ORKEON_<NAME>`), `Bearer` (Azure AD or OIDC), mutual TLS — see [Security](security.md#a2a-mutual-tls). The card stays public; the task endpoints require the credential. The start is refused with exit code 78 when the section exposes no crew, or a crew `Orkeon:Host:Crews` does not declare; when `Host` is not an `http://` or `https://` host name or `Port` is outside 1–65535; and when the host listens beyond the loopback interface (anything but `localhost`, a `127.x.x.x` address or `[::1]`) while `A2A:Security` declares neither an authentication scheme nor mutual TLS. `A2A:EnableServer`, `A2A:Host` and `A2A:Port` — the C# hosts' switches — are refused too, naming their `Orkeon:Host:A2A` replacements: the daemon reads only its own.

```json
{
  "A2A": {
    "Security": { "AllowedAuthSchemes": [ "ApiKey" ], "ApiKeySecretNames": [ "A2A_PEER_KEY" ] }
  },
  "Orkeon": { "Host": { "A2A": { "Enabled": true, "Host": "http://+", "Port": 5002, "Crews": [ "veille" ] } } }
}
```

A peer then sends `Authorization: ApiKey <key>`, the key being the value of `ORKEON_A2A_PEER_KEY` in the service's environment.

**Order.** The A2A server starts after the MCP servers are connected — a task loads a crew, whose tools must be there — and before the chat channel, so it stops after the drain: a run in flight during the grace period still delivers its answer to the peer that asked, while a task that arrives during that grace is refused — the host is stopping — and starts nothing.

---

## 6. Installing it

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

`TimeoutStopSec` is deliberately longer than `ShutdownGracePeriod`, so runs in flight get their grace before systemd loses patience. **Raise one without the other and the one left behind stops meaning anything.** On a stop the host first closes admission: **from that moment no run starts** — a chat message or an A2A task that arrives during the grace is answered "The host is stopping: this run was not started. Send it again once the host is back.", without an acknowledgement or a Stop button, and an A2A peer reads it as `Failed`; nothing is loaded, no model is called. It then waits up to `ShutdownGracePeriod` for the runs in flight — the channel and the A2A server stay up meanwhile, to deliver their answers, and `/status` and `/stop` keep answering —, then stops every one of them and gives them five more seconds to wind down; the generic host's own shutdown budget is set to the grace period plus ten seconds to cover both.

The log is quiet by default: the runner host logs at **Warning**, so the Information lines — each hosted crew at start, each run started and finished, the Discord connection — only appear once the settings raise the level, for instance `"Logging": { "LogLevel": { "Orkeon": "Information" } }`. A level is one of `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`, any case: another word refuses the start with exit code 78, naming its key — it used to crash the daemon, which systemd restarted every ten seconds.

Secrets go in `/etc/orkeon/orkeon-host.env`, readable only by the service user. The unit file stays free of them.

### Windows

The **full** archive (`orkeon-<version>-win-x64.zip` — not the CLI zip) carries
the daemon and its deployment assets. The real executable is
`libexec\orkeon-host\orkeon-host.exe`; `bin\orkeon-host.cmd` is a terminal
wrapper — never register the wrapper with the SCM. The registration script
ships in the archive's `deploy\windows\` folder.

Two channels install the same service. The quickest is the dedicated
**per-machine MSI** — `orkeon-host-<version>-win-x64.msi`, a separate product
from the per-user CLI MSI — which lays out the same paths and registers the
same service declaratively (double-click, or `msiexec /i ... /qn`). Everything
below about the account, the paths, secrets, recovery and the event log holds
for both channels; only the registration mechanics differ.

The MSI is **not code-signed**. Windows shows *"Windows protected your PC"* with an unknown
publisher when you double-click it — **More info** → **Run anyway** — and the elevation
prompt names no publisher either. Check the file first, against `SHA256SUMS.msi` and its
build attestation: [Verify what you install](../guides/verify-what-you-install.md).

For the script channel: extract the archive under `C:\Program Files\Orkeon`,
put your configuration under `C:\ProgramData\Orkeon` (the mirrors of
`/opt/orkeon` and `/etc/orkeon`), and run the bundled script — those paths are
its defaults:

```powershell
.\deploy\windows\install-service.ps1 -EnvironmentSecrets @{ ORKEON_DISCORD_TOKEN = '...' }
Start-Service -Name Orkeon
```

The service runs as the virtual account `NT SERVICE\Orkeon` — the mirror of
the unit's `User=orkeon`: no password to manage, its own SID, Modify on
`ProgramData\Orkeon` and read-only everywhere else. The registration passes
`--working-dir C:\ProgramData\Orkeon`, so relative paths in the settings file
— crew directories included — resolve there, the mirror of
`WorkingDirectory=/var/lib/orkeon`. (A Windows service is born in `System32`;
the flag is how it leaves it.)

No password is ever passed for that account, and that is a requirement rather
than a convenience: the SCM demands a *NULL* password for a virtual account,
and an *empty* password is a different value. Give it the empty one and it
validates the name down the ordinary-account path and refuses it with error
1057, *the account name is invalid or does not exist* — about a name that is
perfectly valid. The MSI channel never meets this (its `ServiceInstall` table
leaves the password null); the script matches it by omitting `password=`
altogether. Registration also sets the service SID type
(`sc.exe sidtype Orkeon unrestricted`); `restricted` is the next hardening
step, untested here.

Secrets go in the service's own `Environment` value (`REG_MULTI_SZ` under the
service key) — `-EnvironmentSecrets` writes it — which only the SCM reads and
only administrators open: the closest mirror of `EnvironmentFile`. Restart the
service after changing them, same contract as systemd.

Recovery mirrors the systemd policy as closely as the SCM allows: restart on
crash twice, then stop. The SCM cannot filter exit codes, so there is no
equivalent of `RestartPreventExitStatus=78` — but a refused configuration ends
in an orderly stop, which crash-only recovery never restarts, and the refusal
is written to the **Application event log** (source `Orkeon`), the one place a
service operator actually reads. `install-service.ps1 -Uninstall` removes the
service and leaves `ProgramData\Orkeon` to you.

With A2A enabled, the listener goes through HTTP.sys, which lets an account that
is not an administrator listen only on a URL prefix reserved for it: the prefix
`Orkeon:Host:A2A` describes — `Host`, `Port` and a final slash,
`http://localhost:5002/` by default, `http://+:5002/` for every interface. The
script reserves it when it registers the service:
`install-service.ps1 -A2AUrlPrefix http://+:5002/` runs `netsh http add urlacl`
for `NT SERVICE\Orkeon` (a reservation the service already holds is kept; one
another account holds fails the registration), records the prefix under the
service key, and `-Uninstall` — or a re-registration — removes it. Run the
script again with the new prefix after changing `Host` or `Port`. The MSI
reserves nothing: it installs before any configuration exists, so it cannot know
the prefix — its closing screen gives the step, to run once as an administrator:

```powershell
netsh http add urlacl url=http://+:5002/ user="NT SERVICE\Orkeon"
```

A service started without its reservation does not crash: the start is refused
with exit code 78, and the Application event log carries the exact command,
prefix and account included. Any other refusal of the listener — a port another
process holds, a port under 1024 without the privilege — names the prefix and
`Orkeon:Host:A2A:Port`.

### Container

[`deploy/Dockerfile.host`](https://github.com/orkeon/orkeon/blob/main/deploy/Dockerfile.host). The token is passed by name at run time, never baked into a layer. The image keeps the license and the third-party notices of what it redistributes under `/usr/share/doc/orkeon/` (`LICENSE.md`, `THIRD-PARTY-NOTICES.md`); the .NET runtime is the base image's, which carries its own. The image exposes no port and declares no `HEALTHCHECK`: the daemon's only HTTP surface is the A2A server, off by default. To expose crews from a container, set `Orkeon:Host:A2A:Host` to `http://+` — the loopback of a container is unreachable from outside it, so `A2A:Security` must declare an authentication scheme — and publish the port (`-p 5002:5002`).

---

## 7. What ships, and what does not

**Ships**: the host and its lifetime, the crew registry with per-run isolation and a concurrency ceiling, the gateway ports, the allow-list authorizer, thread-is-run routing, the throttled responder, the Discord channel with registered `/status` and `/stop` slash commands and the stop button — one authorized path for all three — and the opt-in A2A server, one skill per exposed crew.

**Does not ship**, and is not implied anywhere: a scheduler, hot configuration reload, adding or removing hosted crews while the daemon runs, every chat channel other than Discord, and any HTTP surface beyond the A2A server — no API, no health endpoint (the health checks the telemetry registers are exposed by nothing), no A2A task records (`GET /a2a/tasks/{id}` answers `501`). One runner feature stays out of the daemon too: the `semantic_search` tool is not registered (the MCP servers of the `MCP` section are connected at startup, like a run's — see [MCP integration](mcp.md)). The gateway ports are shaped so the run event bus's JSONL protocol is a legitimate implementation of the same contract — the model is not closed around chat — but that channel is not written.

**One thing cannot be verified in CI**: the specification's own acceptance criterion — launching a crew from a real Discord thread, watching it progress, stopping it by button, with the service running as a systemd daemon. It needs a Discord account and a server, which is an owner action. What CI does hold is everything either side of the socket: the message translation, the two platform limits, authorization, routing, throttling and isolation.

---

## 8. See also

- [The run event bus](run-event-bus.md) — the protocol a watching process reads, and the shape the gateway's ports were written to accommodate.
- [EventHub and crew lifecycle](event-hub-and-crew-lifecycle.md) — inter-crew messaging and its ACL.
- [Publication matrix](../reference/publication-matrix.md) — where `orkeon-host` ships.
