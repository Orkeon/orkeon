> 🇫🇷 [Version française](../fr/getting-started/give-your-agents-a-mailbox.md)

# Give your agents a mailbox

> **See also**: [E-mail tools](../guides/email.md) · [Write a crew in TypeScript](../guides/write-a-crew-in-typescript.md) · [CLI reference](../reference/cli.md#orkeon-email) · [Back to the index](../INDEX.md)

In about fifteen minutes you will run an agent that triages a real Gmail inbox: it reads the
unread mail, files each message into a folder by kind, and writes the replies as **drafts** a
human reviews and sends. Nothing is sent. This page is the short path through one bundled
example; the [E-mail tools guide](../guides/email.md) is the full reference.

## What you get

The example is [`13-email-triage.ork.ts`](https://github.com/orkeon/orkeon/blob/main/examples/scripting/13-email-triage.ork.ts)
and its settings file [`13-email-triage.appsettings.json`](https://github.com/orkeon/orkeon/blob/main/examples/scripting/13-email-triage.appsettings.json).
Three rules hold the e-mail tools together, and the example leans on all three:

- **An agent only names an account.** Servers, credentials and rights are the operator's settings (`Orkeon:Tools:Email`).
- **The account's `Rights` decide what an agent may do**, and sending stays closed until `Send:AllowedRecipients` lists who may receive mail.
- **Received mail is data, never instructions.** Every read result carries the verdict of a prompt-injection screen.

## Prerequisites

| You need | Notes |
|---|---|
| Orkeon | A clone of the repository, or the installed `orkeon` tool — see [Three ways to run Orkeon](./three-ways-to-run-orkeon.md). A `.ork.ts` script also needs esbuild: the release archives ship it, a clone installs it on its first build, and the dotnet tool does not ship it — `npm install -g esbuild` ([where it is looked up](../architecture/scripting.md#configuration-and-toolchain); `orkeon doctor` checks it). |
| A model | The example's settings point at a local [Docker Model Runner](https://docs.docker.com/desktop/features/model-runner/) (`ai/granite-4.0-h-tiny` on `localhost:12434`). Any profile of [`examples/appsettings/`](https://github.com/orkeon/orkeon/blob/main/examples/appsettings/README.md) works: a run resolves **one** settings file, so copy that profile's `Llm` section into the example's file. |
| A Gmail account | With **2-Step Verification** turned on — app passwords do not exist without it. |

## 1. Create a Gmail app password

1. Open <https://myaccount.google.com/apppasswords>, create an app password named `Orkeon`,
   and copy the 16 letters Google shows — **without** the spaces between the groups. If Google
   says app passwords are not available for your account, use
   [Gmail with OAuth2](../guides/email.md#gmail-with-oauth2) instead.
2. Export it in the shell that will run the example:

   ```bash
   export TRIAGE_GMAIL_APP_PASSWORD='abcdefghijklmnop'      # bash / zsh
   ```

   ```powershell
   $env:TRIAGE_GMAIL_APP_PASSWORD = 'abcdefghijklmnop'      # PowerShell
   ```

## 2. The settings

The example's settings file declares one account under `Orkeon:Tools:Email` (its `Llm`
section, and the `Scripting` time limit covered in step 4, are left out here). Replace the address with yours:

> In Orkeon Studio the same account is declared in a form, **Settings › E-mail**, which writes
> this section into your own settings file — see the [e-mail guide](../guides/email.md#quick-start-gmail-with-an-app-password).

```json
"Orkeon": {
  "Tools": {
    "Email": {
      "DefaultAccount": "triage",
      "Accounts": {
        "triage": {
          "Provider": "Gmail",
          "Address": "your.name@gmail.com",
          "Rights": "Read, Organize, Draft",
          "Auth": {
            "Method": "Password",
            "PasswordEnvVar": "TRIAGE_GMAIL_APP_PASSWORD"
          }
        }
      }
    }
  }
}
```

- **`Provider: Gmail`** is a preset: it fills in `imap.gmail.com:993` for reading and `smtp.gmail.com:465` for sending, both TLS.
- **`PasswordEnvVar`** *names* the environment variable that holds the password. The secret is never in the file, and never a tool argument — tool arguments are logged. The variable may have any name.
- **`Rights`** is mandatory. `Read, Organize, Draft` lets the agent search and read, create folders and move mail, and save drafts — exactly what the script uses. It cannot send, delete or purge anything; a call outside the rights fails with a message naming the missing right.
- **`DefaultAccount`** is the account a call that names none uses. With a single account it could be left out; the script names no account at all.

## 3. Check the account from the terminal

The `orkeon email` commands are the operator's side of the family — agents never run them.
From a clone, replace `orkeon` with `dotnet run --project src/scripting/Orkeon.Scripting.Cli --`.

```bash
orkeon email accounts --settings examples/scripting/13-email-triage.appsettings.json
orkeon email check triage --settings examples/scripting/13-email-triage.appsettings.json
```

- `accounts` opens no connection: it lists `triage (default)`, its address, how it reads and sends, its rights, and says `ready` — or `NOT READY:` followed by what to fix (typically the variable that is not set in this shell).
- `check` connects, authenticates and lists the folders, then prints `E-mail account 'triage' is reachable:` with the number of folders and the inbox counts.

Exit codes: `0` success, `1` something you fix (the settings, an unknown account, a variable
not set), `2` what the server or the network did (a refused password, a connection failure),
`130` Ctrl+C. The settings file in use is named on stderr (`Using settings: …`).

## 4. Run the triage

From a clone, at the repository root:

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/13-email-triage.ork.ts \
  --settings examples/scripting/13-email-triage.appsettings.json
```

With the installed tool, put the two example files side by side and run:

```bash
orkeon run 13-email-triage.ork.ts --settings 13-email-triage.appsettings.json
```

What the script does, in order:

1. It stops at once if the settings declare no model — the echo that answers without one cannot sort mail.
2. It creates the folders `Triage/Reply`, `Triage/Read`, `Triage/Newsletters` and `Triage/Review` (labels on Gmail). An existing folder is left as it is, so every run can do this.
3. It takes one page of the inbox: the ten newest **unread** messages (`email_search`). Reading them (`email_read`) leaves them unread.
4. A message the prompt-injection screen does not rate `clean` never reaches the model: it goes to `Triage/Review`. The others are handed to the model fenced, as data, and sorted into `reply`, `read` or `newsletter`; any other answer also goes to `Triage/Review`.
5. For a `reply`, the model writes a short answer and the script saves it as a reply draft (`email_draft`) — it waits in Gmail's Drafts.
6. Each message is then moved to its folder (`email_move`), one at a time: a run that stops half-way has filed what it finished, and the next run starts on the rest.

The run ends by printing its result as JSON on stdout — the agent's summary line, the count
filed per kind, and the number of drafts:

```json
{
  "result": {
    "output": "sorted 10 message(s), 3 draft(s) to review",
    "filed": { "reply": 3, "read": 4, "newsletter": 2, "review": 1 },
    "drafts": 3
  }
}
```

The numbers above are illustrative. Open Gmail: the inbox has lost those messages, the
`Triage/…` labels hold them, and Drafts holds the replies, for you to edit and send — or not.

> **A time limit applies.** The scripting sandbox bounds a whole run to 30 seconds of
> wall-clock time by default, the awaited mail and model calls included, and ten messages
> through a local model take longer. The example's settings file raises the bound to ten
> minutes, `"Orkeon": { "Scripting": { "Limits": { "ExecutionTimeout": "00:10:00" } } }`
> next to `Tools`; keep that block when you write your own settings file. Messages filed
> before a stop stay filed.

## 5. Adapt it

- **Other folders, other kinds.** The `folders` object at the top of the script maps each kind to a folder, and `kinds` lists what the model may answer; change both, and the prompt that names the kinds. `emailSearch` takes more criteria (`from`, `subject`, `since`, `limit` up to 50…) — the [parameters](../guides/email.md#parameters) list them. In a script, tool names are camelCased (`tools.emailSearch`) but argument keys keep the tools' snake_case (`unread_only`, `reply_to_id`).
- **Let it send — safely.** Add `Send` to `Rights` **and** list the recipients: until `Send:AllowedRecipients` holds at least one entry, `email_send` refuses every message. Entries are addresses, `*@domain` or `*`; To, Cc and Bcc are all checked, and `MaxRecipients` / `MaxPerHour` cap the volume. Drafts stay the better path for anyone outside your own addresses. See [Rights and the send allow-list](../guides/email.md#rights-and-the-send-allow-list).

  ```json
  "Rights": "Read, Organize, Draft, Send",
  "Send": { "AllowedRecipients": [ "your.name@gmail.com" ], "MaxPerHour": 10 }
  ```

- **From a YAML crew instead.** The runner host behind `orkeon run` registers the thirteen `email_*` tools, so a YAML agent lists them by name — `tools: [email_accounts, email_search, email_read, email_move, email_draft]` — and the model decides the calls. Declare the accounts in **that crew's own** settings file (it holds the crew's `Llm` section too), not in an `appsettings.json` of the directory you run from, nor in environment variables: those reach every run. See [From a YAML crew](../guides/email.md#from-a-yaml-crew). `orkeon forge` never puts the mailbox tools in the crews it forges.

## Other mailboxes

- **Hotmail and Outlook.com** go through Microsoft Graph with OAuth2: register an application once, then sign in once with `orkeon email login <account>` — [Hotmail and Outlook.com (Microsoft Graph)](../guides/email.md#hotmail-and-outlookcom-microsoft-graph).
- **Gmail without an app password**: [Gmail with OAuth2](../guides/email.md#gmail-with-oauth2).
- **Your own server**, over IMAP or POP3 and SMTP, with the `Custom` preset: [Your own server (IMAP, POP3, SMTP)](../guides/email.md#your-own-server-imap-pop3-smtp).
- **Where OAuth tokens are stored**, and what that protects: [Where the tokens live](../guides/email.md#where-the-tokens-live).
- **Something refuses?** Every error names its fix; the usual ones are in [Troubleshooting](../guides/email.md#troubleshooting).

> **Gmail is campaigned; Hotmail is campaign-pending.** The path of this tutorial — a Gmail
> app password — was run against a real account on 2026-10-04, and Gmail with OAuth2 on
> 2026-10-05 (MAIL-07): see
> [What the Gmail campaign established](../guides/email.md#what-the-gmail-campaign-established).
> Hotmail and Outlook.com have not been run against a real account yet.
