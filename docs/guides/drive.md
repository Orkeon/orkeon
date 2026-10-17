> 🇫🇷 [Version française](../fr/guides/drive.md)

# Drive tools

> **See also**: [Tool inventory](../tools/inventory.md#drive-tools-orkeontoolsdrive--addorkeondrivetoolsconfiguration) ·
> [E-mail tools](email.md) · [Configuration reference](../reference/configuration.md#orkeontoolsdrive) ·
> [ADR-013](../adr/ADR-013-drive-tool-family.md) · [Back to index](../INDEX.md)

The drive family gives agents a cloud drive: **Google Drive** ("My Drive") through the Drive API
v3, and **OneDrive** — personal, or for work and school — through Microsoft Graph. Both are spoken
in plain HTTP, with no vendor SDK. An agent lists, searches, reads, downloads into the virtual file
system, uploads from it, creates folders, moves, renames, deletes and shares — within what the
operator allows:

- **The operator declares the accounts** under `Orkeon:Tools:Drive`. An agent names an account
  (or uses the default one), never a client id, a secret or a token.
- **`Rights` is mandatory** for each account: `Read, Write, Organize, Share, Delete, Purge`.
- **`Root` confines an account to a folder** of its drive. Every item a call touches must lie
  under it; an item outside is reported as not found.
- **Sharing is closed by default**: an empty `Share:AllowedRecipients` allows nobody, and public
  links stay off until `Share:AllowPublicLinks` is `true`.
- **Every account signs in once with OAuth2** (`orkeon drive login`), and the tools refresh the
  access token by themselves.

> **Not yet campaigned.** The family is covered by offline tests against scripted Google and
> Microsoft APIs. It has not been run against a real Google Drive or OneDrive yet: that campaign
> is the owner's (DRIVE-07). Report what differs.

## The eleven tools

| Tool | Right needed | What it does |
|---|---|---|
| `drive_accounts` | none | Lists the accounts: name, rights, root folder, whether each is ready |
| `drive_list` | Read | One page of a folder (the root by default), folders first |
| `drive_search` | Read | Items matching words, a name, a MIME type, a kind, a date or a folder, inside the root |
| `drive_info` | Read | One item's metadata, its browser address included |
| `drive_read` | Read | The text of a text file or a Google document, screened, in slices |
| `drive_download` | Read + a writable mount | Saves a file into a virtual directory; Google documents are exported |
| `drive_upload` | Write | Uploads a file of the VFS, or replaces a file's content (`replace_id`) |
| `drive_create_folder` | Write | Creates a folder |
| `drive_move` | Organize | Moves and/or renames an item |
| `drive_delete` | Delete (+ Purge with `permanent`) | Moves an item to the trash, or deletes it for good |
| `drive_share` | Share | Gives allowed addresses access, or creates a public link when allowed |

Ids are the provider's own and opaque: pass them back exactly as `drive_list` or `drive_search`
returned them. `root` names the account's root folder wherever a folder id is taken
(`folder_id`, `parent_id`, `destination_id`).

## OneDrive (Microsoft Graph)

### Register an application in Microsoft Entra

1. Open the [Microsoft Entra admin center](https://entra.microsoft.com) › **App registrations** ›
   **New registration**.
2. Name it (`Orkeon drive`, for instance) and choose the supported account types: **Personal
   Microsoft accounts only** for a personal OneDrive, **Accounts in any organizational directory
   and personal Microsoft accounts** to serve both. No redirect URI is needed.
3. In **Authentication**, set **Allow public client flows** to **Yes**: the sign-in uses the
   device-code flow.
4. In **API permissions** › **Microsoft Graph** › **Delegated permissions**, add `Files.ReadWrite`
   (or `Files.Read` for an account that only reads) and `offline_access`. Without
   `offline_access` Microsoft issues no refresh token, and the login refuses to store a sign-in
   that would expire within the hour.
5. Copy the **Application (client) ID**.

An application registered for the e-mail family can serve the drive family too: add the `Files.*`
permissions to it.

### Declare the account and sign in

Under `Orkeon:Tools:Drive:Accounts`:

```json
"pro": {
  "Provider": "OneDrive",
  "Rights": "Read, Write, Organize",
  "Root": "Orkeon/agents",
  "Auth": { "ClientId": "00000000-0000-0000-0000-000000000000" }
}
```

`Auth:Tenant` defaults to `consumers`, the tenant of personal accounts; for OneDrive for work or
school set `"Tenant": "organizations"` or your tenant id. Then, in a terminal:

```bash
orkeon drive login pro
```

The command prints Microsoft's verification address and a code. Open the address in any browser —
on any device —, enter the code, sign in and accept. The command stores the tokens and prints
`Signed in`. Check the account with `orkeon drive check pro`: it finds the root folder and lists
its first items.

## Google Drive

Google's device flow does not offer the Drive scopes, so the login prints the address of Google's
consent page for you to open, and receives the answer on a loopback address (`127.0.0.1`), with
PKCE — the same flow as a Gmail account with OAuth2.

1. In the [Google Cloud console](https://console.cloud.google.com), create a project and enable the
   **Google Drive API**.
2. Configure the **OAuth consent screen**: user type **External**, add the scope
   `https://www.googleapis.com/auth/drive` (or `drive.readonly` for an account that only reads),
   and add your own address as a **test user**.
3. Create an **OAuth client ID** of type **Desktop app**. Copy its client ID, and put its client
   secret in an environment variable (`GOOGLE_DRIVE_CLIENT_SECRET` below).
4. Declare the account, under `Orkeon:Tools:Drive:Accounts`:

   ```json
   "perso": {
     "Provider": "GoogleDrive",
     "Rights": "Read, Write",
     "Root": "Orkeon",
     "Auth": {
       "ClientId": "123456789012-abcdefghijklmnop.apps.googleusercontent.com",
       "ClientSecretEnvVar": "GOOGLE_DRIVE_CLIENT_SECRET"
     }
   }
   ```

5. Run `orkeon drive login perso`. It prints a Google address to open and listens on a free port
   of `127.0.0.1`. Sign in and accept; Google shows a warning screen for an application it has not
   verified, which you may continue past for your own application. **When the browser runs on
   another machine** — WSL, a container, an SSH session — the last page cannot load: copy the
   address it ends on (`http://127.0.0.1:…/?state=…&code=…`) from the address bar, paste it in the
   terminal and press Enter.

**Keep the sign-in alive.** A Google application left in the **Testing** publishing status gets
refresh tokens that expire after 7 days. Publish it (**In production**) to keep them.

## Settings reference

| Key (under `Orkeon:Tools:Drive`) | Meaning |
|---|---|
| `DefaultAccount` | The account a call that names none uses; optional with a single account |
| `CredentialsDirectory` | Where the tokens are kept (its `drive` subdirectory); default `credentials` next to the per-user settings |
| `Screening:WithholdRejected` | Withhold the text of a file the prompt-injection screen rejects (default `false`: flag only) |
| `Accounts:<name>:Provider` | `GoogleDrive` or `OneDrive` — mandatory |
| `Accounts:<name>:Rights` | Of `Read, Write, Organize, Share, Delete, Purge` — mandatory |
| `Accounts:<name>:Root` | The folder the account is confined to, as a path from the root of the drive (`Orkeon/agents`); unset: the whole drive |
| `Accounts:<name>:Auth:ClientId` | The Google Cloud or Microsoft Entra application — mandatory |
| `Accounts:<name>:Auth:ClientSecretEnvVar` | The **name** of the variable holding the Google client secret — mandatory for Google Drive |
| `Accounts:<name>:Auth:Tenant` | Microsoft only: `consumers` (default), `organizations`, `common` or a tenant id |
| `Accounts:<name>:Share:AllowedRecipients` | Who an agent may share with: an address, `*@domain` or `*`; empty: nobody |
| `Accounts:<name>:Share:AllowPublicLinks` | Whether an agent may create a link anyone holding it can open (default `false`) |
| `Accounts:<name>:MaxUploadMegabytes` | The largest upload (default 100) |
| `Accounts:<name>:TimeoutSeconds` | HTTP timeout of the account's calls |

The section is validated lazily, as the e-mail one: a broken account is reported when a call or
`orkeon drive` uses it, and never breaks a crew that opens no drive. The client secret is read
like the e-mail secrets: the process environment, then — on Windows — the user's own variables.

## Rights, root folder and scopes

| Right | Tools it opens |
|---|---|
| `Read` | `drive_list`, `drive_search`, `drive_info`, `drive_read`, `drive_download` |
| `Write` | `drive_upload`, `drive_create_folder` |
| `Organize` | `drive_move` |
| `Share` | `drive_share` |
| `Delete` | `drive_delete` (to the trash) |
| `Purge` | `drive_delete` with `permanent: true` |

- **An account that only reads asks for a read-only scope** (`drive.readonly`, `Files.Read`): a
  leaked token of a reading crew cannot write either. Granting `Write` later changes the scopes,
  and the account asks for a new sign-in.
- **`Root`** is found once per run: by path on OneDrive, by name under each parent on Google Drive
  (which has no paths). An item is inside the root when its parent path says so (OneDrive) or when
  climbing its parents reaches the root (Google Drive, 32 levels at most). A search on Google Drive
  cannot be bounded to a subtree: its results outside the root are dropped, so a page can hold
  fewer items than `limit`.
- **A name already taken in a folder is refused** on upload, folder creation and move: Google Drive
  would accept two items of the same name, and an agent addressing files by name would then pick
  the wrong one.
- **The trash first.** `drive_delete` moves to the trash (the recycle bin on OneDrive), from where a
  person restores it. `permanent: true` needs `Purge`; a personal OneDrive has no permanent
  deletion and refuses it.
- **A crew whose drive tools need a right no ready account grants is refused at start**, before
  any model call, with a line per tool — as for the mail tools.

## What reaches the agent, and what never leaves

- **The token stays with the API.** It is sent to `www.googleapis.com` or `graph.microsoft.com`
  only, over HTTPS. A OneDrive download goes through the pre-authenticated
  `@microsoft.graph.downloadUrl` without it, an upload session receives the bytes without it, and a
  paging cursor that does not point at the API is refused.
- **Content is data.** `drive_read` opens with a notice that the text is untrusted, and the verdict
  of the prompt-injection screen (`clean`, `suspicious`, `rejected`). With
  `Screening:WithholdRejected` the text of a rejected file is withheld. Names and metadata of the
  other tools go through the run's usual result sanitizer.
- **Downloads never overwrite.** `drive_download` writes a new file (`name (2).ext` when the name is
  taken) into a mount the crew may create in.
- **Results fit the agent loop.** A page is asked again with fewer items until it fits a tool
  result, so `next_cursor` resumes exactly after the last item shown; `drive_read` returns slices
  with `next_offset`.

## Using the tools

From a YAML crew:

```yaml
agents:
  archivist:
    role: "Archivist"
    goal: "File the weekly report in the team drive"
    tools: [drive_list, drive_upload, drive_move]
```

From an `.ork.ts` script, the tools are typed (`Typings/tools.d.ts`), with the tool's own
snake_case keys:

```ts
const page = await tools.driveList({ folder_id: "root" });
const report = await tools.driveUpload({ path: "/output/report.pdf", folder_id: page.items[0].id });
```

## Commands

```bash
orkeon drive accounts [--json]     # the accounts, their rights, root folder and readiness (no network)
orkeon drive login <account>       # the one interactive step: device code (OneDrive) or browser (Google)
orkeon drive check <account>       # connects, finds the root folder, lists its first items
orkeon drive logout <account>      # forgets the stored tokens
```

Every verb takes `--settings <file>`; without it the settings are resolved like `orkeon run`
resolves them, so the tokens land where every run looks for them.

## Where the tokens live

The runner keeps them under its internal `/credentials` root, which no agent-facing tool resolves,
in a `drive` subdirectory created owner-only on Unix — beside the e-mail family's `email` one, in
the same credentials directory. As for e-mail, the tokens are shielded from the VFS tools, **not**
from a `shell_command` running as the same operating-system user. `orkeon-repl` keeps no token
store: a drive tool called from the REPL says so.

## Out of scope for now

Google shared drives and SharePoint document libraries (an account sees its own "My Drive" or
OneDrive), revisions, comments, removing a share, copying, and a forge trial drive — the forge
removes the drive tools from a forged crew. A drive mounted in the virtual file system is not
offered either: the tools are the interface.
