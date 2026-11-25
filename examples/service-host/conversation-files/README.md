# Files in a chat conversation

A hosted crew that works on the files people send it, on Discord or in the personal chat of a
Teams bot, and hands a report back. `crew/crew.yaml` is the crew, `appsettings.host.json` the daemon.

What it shows:

- `Orkeon:Host:Files` gives each conversation `/inbox` (read-only, what was received) and `/outbox`
  (read-write, what is handed back). Nothing mounts them by hand, and no other conversation has them.
- The crew's `files:` block opens one more folder, `/output/shared`, in both directions: `/get`
  takes from it, `/put` leaves a file there. Delete the block and `/put` finds no folder; the change
  is read at the next command, without a restart. That folder is the crew's: **every thread of the
  crew reads it.**
- `chat_attach` sends a file to the thread of the run. Without it the files are named at the end of
  the answer, and `/get` fetches them.
- The agent has no shell and no network tool: a received file may hold instructions meant for it.

## Before you start

1. Put your own Discord user id in `Discord:AllowedUserIds`: empty, the start is refused.
2. `mkdir -p data/output/shared`. `data/` holds the conversations too, so keep it out of git. The
   crew is in a folder of its own because the host mounts a crew's folder, and refuses to start when
   that folder holds the conversations.
3. Export the two secrets the settings name: `ORKEON_DISCORD_TOKEN` and `DEEPSEEK_API_KEY`.
4. For Teams, set `Enabled` to `true` there and replace the ids; the registration, the manifest and
   the certificate are in [Files in Teams](../../../docs/architecture/service-host.md#files-in-teams).
   Remove `CertificatePathEnvironmentVariable` when you have no certificate: a name whose variable is
   unset refuses the start.

## Run it

```bash
# From this folder.
orkeon-host --settings ./appsettings.host.json

# From a checkout, without an installed binary.
dotnet run --project ../../../src/hosting/Orkeon.Host -- --settings ./appsettings.host.json
```

In a thread, post a PDF with "summarise this". Then try `/files`, `/get /outbox/report.md`, and
`/put` with a file and `/output/shared`.

Without a daemon, the same crew works on two folders of your own (`chat_attach` then answers that
there is no conversation):

```bash
mkdir -p in out && echo "Notes to summarise." > in/notes.txt
orkeon run crew/crew.yaml --mount ./in:/inbox:ro ./out:/outbox:rw ./data/output:/output:rw \
  --initial-context "Summarise /inbox/notes.txt"
orkeon run crew/crew.yaml --validate
```

What this opens, and what it does not protect: [Conversation files](../../../docs/architecture/security.md#conversation-files).
