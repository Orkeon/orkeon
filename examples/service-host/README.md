# Host a crew as a service (configuration example)

`appsettings.host.json` is a complete configuration for `orkeon-host`: one hosted crew, a
Discord channel, mounts, and an LLM provider. Copy it, fill in the three things only you know,
and the daemon runs.

Full explanation: [The service host and the chat gateway](../../docs/architecture/service-host.md).

## The three things you have to change

**`AllowedUserIds` is empty on purpose, and an empty list denies everyone.** The channel refuses
to start rather than answering nobody in silence. Put your own Discord user id in it — the
opposite default is how a bot invited to a public server ends up spending your API budget on
strangers.

**`Path`** must point at your crew. It accepts what `orkeon run` accepts: a YAML file, a
multi-file crew directory, or an `.ork.ts` script.

**The mounts** describe what the crew may read and write, in virtual paths. Everything a crew
touches goes through them.

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
button, reports progress every couple of seconds, and delivers the answer. `/status` says what
the thread is running; `/stop` and the button do the same thing.

## What this does not do

It does not schedule anything. rc.2 ships no scheduler: a crew that should run every morning
still needs the artifact `orkeon forge promote --schedule` produces, installed by you. The host
is where such a crew would live, not what would trigger it.
