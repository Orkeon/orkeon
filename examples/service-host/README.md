# Host a crew as a service (configuration example)

`appsettings.host.json` is a complete configuration for `orkeon-host`: one hosted crew, a
Discord channel, the same crew offered to other agents over A2A, mounts, and an LLM provider.
Copy it, fill in the three things only you know, and the daemon runs.

Full explanation: [The service host and the chat gateway](../../docs/architecture/service-host.md).

## The three things you have to change

**`AllowedUserIds` is empty on purpose, and an empty list denies everyone.** The channel refuses
to start rather than answering nobody in silence. Put your own Discord user id in it — the
opposite default is how a bot invited to a public server ends up spending your API budget on
strangers.

**`Path`** must point at your crew. It accepts what `orkeon run` accepts: a YAML file, a
multi-file crew directory, or an `.ork.ts` script — with one caveat for the script form:
transpiling `.ork.ts` needs esbuild on the machine, and neither the container image nor a bare
service install carries it. A daemon-hosted crew is a YAML crew unless you install esbuild.

The host validates all of this at start: a wrong path, an empty allow list or an unset token
variable refuse the start with exit code 78 — before the service reports ready — and the
systemd unit deliberately does not restart on that code, so the message stays on top of the
journal instead of scrolling away every ten seconds.

**The mounts** describe what the crew may read and write, in virtual paths. Everything a crew
touches goes through them. The crew definitions themselves need no mount: the host mounts each
configured crew's directory read-only, automatically.

## The two secrets, and where they are not

`ORKEON_DISCORD_TOKEN` and `DEEPSEEK_API_KEY` are **named** in this file, never written in it.
Their values live in the environment:

```bash
# systemd: /etc/orkeon/orkeon-host.env, readable only by the service user
ORKEON_DISCORD_TOKEN=…
DEEPSEEK_API_KEY=…
```

A token in a configuration file ends up in a commit; a token in a container image stays in that
layer for as long as the image exists, including after someone "removes" it in a later one. A
bot token can read every message a server sends, so it does not get an exception to a rule the
LLM providers already follow.

## Running it

```bash
# In a terminal first — a daemon you cannot run in the foreground is one you cannot debug.
orkeon-host --settings ./appsettings.host.json

# Then as a service.
sudo cp ../../deploy/systemd/orkeon-host.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now orkeon-host
journalctl -u orkeon-host -f
```

From a Discord thread, say what you want; the bot acknowledges immediately with a **Stop**
button, reports progress every couple of seconds, and delivers the answer. `/status` and
`/stop` are registered slash commands (autocompleted, answered ephemerally); the button is
the same stop with a different finger, allow-list check included. Commands registered
globally can take up to an hour to appear on Discord's side — list your server in
`Discord:GuildIds` to get them immediately.

## Reaching the crew from another agent (A2A)

`Orkeon:Host:A2A` exposes `support` to other agents: the daemon serves an agent card listing one
skill per exposed crew, and a task sent to that skill is a run of the crew — the same run a Discord
thread starts, under the same mounts and the same `MaxConcurrentRuns`. A crew left out of
`A2A:Crews` is invisible to peers.

```bash
curl http://localhost:5002/.well-known/agent.json
curl -X POST http://localhost:5002/a2a/tasks/send -H 'Content-Type: application/json' \
     -d '{"id":"t-1","skillId":"support","input":"How do I reset my password?"}'
```

`send` answers once the run is over. To follow it as it goes, `sendSubscribe` streams server-sent
events — `curl -N` prints each one as it arrives:

```bash
curl -N -X POST http://localhost:5002/a2a/tasks/sendSubscribe -H 'Content-Type: application/json' \
     -d '{"id":"t-2","skillId":"support","input":"How do I reset my password?"}'
```

The stream opens on `Working`, then carries one `Working` update per line a Discord thread reads —
`Running 'support'…`, then `✔ <role> — step N done` for each finished task — in its `message`, then
the final state (`Completed`, the answer in `partialOutput`) and `data: [DONE]`. A task sent while the
host is stopping starts nothing: it answers `Failed`, "The host is stopping: this run was not
started. Send it again once the host is back."

The example listens on the loopback, where nothing else can reach it, so it needs no credential.
To serve peers on other machines, listen on every interface (`"Host": "http://+"`) and require a
credential — the host refuses to start otherwise:

```json
"A2A": {
  "Security": { "AllowedAuthSchemes": ["ApiKey"], "ApiKeySecretNames": ["A2A_PEER_KEY"] }
}
```

at the top level of the file, next to `Llm`, with the key itself in `ORKEON_A2A_PEER_KEY`; a peer
then sends `Authorization: ApiKey <key>`. Remove the `A2A` block under `Orkeon:Host` to keep the
daemon off the network altogether.

## What this does not do

It does not schedule anything. rc.2 ships no scheduler: a crew that should run every morning
still needs the artifact `orkeon forge promote --schedule` produces, installed by you. The host
is where such a crew would live, not what would trigger it.
