> 🇫🇷 [Version française](../fr/adr/ADR-021-chat-tool-family.md)

> **See also**: [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-012](./ADR-012-email-tool-family.md) · [ADR-013](./ADR-013-drive-tool-family.md) · [Back to the index](../INDEX.md)

# ADR-021 — The chat tool family, Microsoft Teams first: a fifth motivated exception to the scope freeze

**Status**: Accepted · **Date**: 2026-11-23
· **Scope**: the `Orkeon.Tools.Chat` family, `Orkeon.Tools.Abstractions/Auth` (the OAuth client it shares with the e-mail and drive families), `Orkeon.Hosting` (the `chat` subdirectory of `/credentials`), `orkeon chat` (`Orkeon.Scripting.Cli`), `Orkeon.Host` (the inbound Teams channel), the `Orkeon.Tools` umbrella

This ADR records the decision and the design. It ships no tool: the tools arrive with the lots that follow it, and those lots amend the counts, the inventory, the limitations page and CONTRIBUTING. Built so far: `chat_accounts`, `chat_list`, `chat_read`, `chat_members`, `chat_send`, `chat_search`, `chat_edit`, `chat_delete` and `chat_react`; an edit, a deletion and a reaction count in `Send:MaxPerHour` and in the pace of a place, like a post.

## Context

Issue #446 asks for Microsoft Teams. An agent run by Orkeon cannot read a channel or a chat, post a result where the team works, nor read the transcript of a meeting. No code names Teams; the tool inventory names it once, to say it is missing ("there is no SMS, push or Teams tool"), and the messaging-gateway design note lists it as the enterprise chat channel of the gateway.

What exists for chat as a tool is two Slack tools named after the vendor, `slack_send_message` and `slack_read_messages`. No shipped root registers them: a bot token passed by a host written in C#, no settings key, no rights, no allow-list.

The scope is frozen (CONTRIBUTING, "The scope is frozen"). The case is the one ADR-012 and ADR-013 made: a `.ork.ts` script runs on Jint and cannot hold an OAuth token nor keep a sign-in, and a plugin would not put the rights, the allow-list and the screening in Orkeon itself.

Most of what a Teams connector needs already ships:

- Microsoft Graph v1.0 over a plain `HttpClient`, for mail and for OneDrive, with no `Microsoft.Graph` package and no MSAL (`EmailDefaults.GraphHost` checks that a paging link stays on the Graph host).
- A shared OAuth client in `src/tools/Orkeon.Tools.Abstractions/Auth/` (`OAuth2Client`, `LoopbackRedirectListener`, `OAuthSignIn`): authorization code with PKCE on a loopback redirect, device code, refresh, tokens kept under the internal `/credentials` root. A sign-in runs the one flow its settings name.
- An account model set by the operator, never by the model: mandatory `Rights`, an allow-list closed when empty, a send cap per hour, secrets given as the names of environment variables, content screened for prompt injection.
- A chat gateway in `orkeon-host` with one channel, Discord (`IChatChannel`, `DiscordChannel`), where an enabled channel with an empty `AllowedUserIds` refuses the start (`ChatChannelService`). The host also has HTTP listeners of its own (the run API, A2A).

## Decision

1. **A fifth motivated exception to the scope freeze: a `chat_*` family of ten tools.** The names say the function, never the vendor, so a second provider (Slack, then others) adds a provider and no tool. The two Slack tools become the second provider in a follow-up and leave the count then. Microsoft Teams is the first provider.

   | Tool | Does | Access |
   |---|---|---|
   | `chat_accounts` | the declared accounts, their provider, rights and sign-in state | Read |
   | `chat_list` | the places of an account: teams, channels, chats | Read |
   | `chat_read` | the messages of a channel or a chat, or the replies of one message | Read |
   | `chat_search` | messages by text, by author, or those that mention the signed-in user | Read |
   | `chat_members` | the members of a place; a person looked up, to target or mention them | Read |
   | `chat_meetings` | the transcripts of a meeting, and the text of one | Read |
   | `chat_send` | a message to a channel, a chat or a person, or a reply | Execute |
   | `chat_edit` | rewrite a message the account sent | Execute |
   | `chat_delete` | delete a message the account sent | Execute |
   | `chat_react` | set or remove a reaction | Execute |

   The access classes are those `email_read` and `email_send` declare, so `plan` mode reads and never posts. A mention is a member id returned by `chat_members`, never a name the model wrote. No tool watches a place for new messages: Graph allows polling for changes once a day at most, and real time is the inbound channel (decision 8). A file is not uploaded by this family: `drive_upload` puts it in the sender's OneDrive and `chat_send` links it.

2. **The operator owns accounts, rights and places; the agent names an account.** Accounts live under `Orkeon:Tools:Chat:Accounts:<name>` with a `Provider`. `Rights` is mandatory (`Read`, `Send`, `Edit`, `Delete`, `React`, `Meetings`); a tool whose right is missing answers with the right to add. A place is written one way in the settings and in a call: `team:<team>`, `team:<team>/channel:<channel>`, `chat:<id>`, `user:<address>` or `user:*@<domain>`, or `*`. Names are resolved to ids when the account is first used, then ids alone are compared; a name that matches nothing or two places sets the entry aside with a message naming it. `Send:AllowedPlaces` and `Read:AllowedPlaces` are both closed when empty (D9), `Send:MaxPerHour` caps the process, and the adapter honours Graph's own limits and `Retry-After`. `Cloud` picks the Graph host and sign-in authority (`Global`, `UsGov`, `UsGovDoD`, `China`); a paging link is followed only on the host of the account. A failure is a typed code with the fix in its message (`RightMissing`, `PlaceNotAllowed`, `ConsentMissing`, `SignInRequired`, `Throttled`, `NotFound`, `NotSupportedByAccount`).

3. **Scopes follow the rights, and admin scopes only when `AdminGranted` names them.** The scopes asked are the ones the account's `Rights` need, never more. Three groups of delegated permissions need a tenant administrator (`ChannelMessage.Read.All` for reading a channel, `ChannelMessage.ReadWrite` for editing or deleting in one, and the meeting-transcript scopes), and Entra refuses a whole consent request when one permission in it needs the administrator. They are therefore never asked by default: `Auth:AdminGranted` lists what the administrator has consented to for this application (`ChannelMessages`, `ChannelEdit`, `Meetings`), each entry adds its scope to the next sign-in, and without it the tool answers `ConsentMissing` with the permission and who grants it, instead of failing on a bare 403. Without an administrator a user still reads and writes their chats and posts in channels. An account holds one token set; a new sign-in asks for the whole set again and replaces it.

4. **Sign-in: loopback first, `--device-code` on request.** `orkeon chat login <account>` uses the browser on a loopback redirect, and the device code only when `--device-code` asks for it (a container, a remote shell). A Microsoft account is given the device code alone today, and a Microsoft-managed Conditional Access policy blocks that flow in many tenants, so the flow is chosen at the sign-in and not fixed by the provider. E-mail and drive accounts keep the flow they have. The family has `login`, `logout` (the tokens are deleted; the refresh token is revoked where the provider has a revocation endpoint, which Microsoft Entra does not: the command says so), `check` and `accounts`. `orkeon-host` has no terminal and never opens a sign-in: a token refused later makes the tools answer `SignInRequired` with the command to run. Tokens go under `/credentials/chat`.

5. **A webhook account for send only.** `"Provider": "TeamsWebhook"` holds `Webhook:UrlEnvVar` (the URL is a secret: its name only), has the `Send` right alone and serves `chat_send` with no sign-in. The URL fixes the place, so `chat_send` takes none and has no `Send:AllowedPlaces` (declaring the account is the opening); `Send:MaxPerHour` applies. It replaces the retired Office 365 connectors with a Power Automate "Workflows" webhook.

6. **A read-only application account.** `"Auth": { "Method": "ClientCredentials" }` with a certificate or a federated token, each named by an environment variable, or a client secret for a tenant that issues nothing else. The shared client's client-credentials grant, today used by the MCP client with a client secret, gains the signed assertion. An application has no chats of its own and joins no team, so it reads what `Read:AllowedPlaces` names and nothing else (`*` is refused), and what means "me", with `chat_search`, answers `NotSupportedByAccount`. Graph refuses an application's posts outside a data migration, so such an account is refused at validation when its `Rights` hold more than `Read` and `Meetings`. A pipeline that must also post pairs it with a webhook account.

7. **A message is untrusted text, and the forge denies the family.** A body is HTML written by anyone who can post in the place, a guest included. It reaches the model rendered to text, screened by the prompt-injection validator the e-mail family uses, flagged by default and withheld under `Orkeon:Tools:Chat:Screening:WithholdRejected`, and cut by `Orkeon:Tools:ResultLimits`. A message Teams marks as a policy violation is returned withheld, never quoted. The forge denies the family to the crews it forges, as it does the drive tools (ADR-013, decision 7): there is no trial tenant.

8. **An inbound channel in `orkeon-host`, with its own listener and no Teams SDK.** `TeamsChannel` implements `IChatChannel` beside `DiscordChannel`, under `Orkeon:Host:Teams` (`Enabled` off by default, the app's id and secret by variable name, `AllowedUserIds` holding Entra object ids, the start refused when it is empty, as for Discord): a thread is a run, the reply goes back in the thread. Unlike Discord, Teams calls the bot, so the host needs an HTTPS endpoint reachable from Microsoft and the check of the token each incoming activity carries. The listener is its own (`Orkeon:Host:Teams:Prefix`), not the run API's nor A2A's; the bot protocol is written by hand over HTTP, as Graph is, and the token is checked with the `Microsoft.IdentityModel.*` packages the repository already pins. No Teams SDK package is added. It is the part with the largest surface, and the last delivered.

9. **Credentials directory.** The chat tokens live in a `chat` subdirectory of the same `/credentials` root as the other two families, with the rule of ADR-013, decision 6.

### Decisions retained (D1–D10)

Each was a question left open by the issue; the recommendation was retained, and each can change without touching the others.

| # | Question | Retained |
|---|---|---|
| D1 | Whose Entra application | Each user registers their own (`Auth:ClientId`), as for mail. No multi-tenant application under Orkeon's name. |
| D2 | Meetings | The join link, or a meeting chat when Graph gives the link with it. The calendar (`Calendars.Read`) stays out. |
| D3 | A channel's files | `chat_send` attaches nothing in a channel at first; SharePoint stays outside ADR-013. |
| D4 | Reactions in a channel | Delivered for chats; in a channel only after a trial on a real tenant, otherwise `NotSupportedByAccount`. |
| D5 | China cloud | Two Microsoft pages give two sign-in hosts: `China` is delivered last, or refused at validation, until a tenant settles it. |
| D6 | Inbound listener and protocol | Its own listener, the bot protocol by hand over HTTP, the token checked with the pinned `Microsoft.IdentityModel.*` packages. No Teams SDK. |
| D7 | A bot for `chat_send` | The bot stays the gateway's alone. |
| D8 | Adaptive cards | Text and Markdown only. |
| D9 | `Read:AllowedPlaces` empty | Closed, like `Send`: one rule. |
| D10 | The two Slack tools | Unchanged here; the second provider in a follow-up. |

## Alternatives considered

- **An MCP server** (Microsoft's Teams server, a community one). Its tools arrive with the access class `Unspecified`, a write to the gate, so `plan` mode refuses the reads too, unless the operator trusts the server's own annotations. The MCP client persists no token, so a device sign-in is asked at every run, and it has no authorization-code flow. No rights per account, no allow-list of places, no screening.
- **A plugin**: full trust, not shipped, and the account model would be rebuilt outside Orkeon.
- **`teams_*` tools**: one family per vendor, as the two Slack tools started; ten more tools at each new service.
- **`http_api` by hand**: no token kept, no refresh, the scopes and the Graph paths left to the model.
- **The webhook alone**: posts into one place, reads nothing.

## Consequences

- Ten tools, a provider per account, and the sign-in, webhook, application-account and inbound-channel parts described above, delivered in lots after this one. Until a lot ships a part, this page states the decision and not the behaviour.
- Pages to amend with the lot that adds the tools: CONTRIBUTING ("The scope is frozen"), the limitations page, the tool inventory (including the line that says there is no Teams tool), the tool counts, the typings, a `chat` guide and the configuration reference.
- No Microsoft 365 tenant is available to the maintainers here: the tools are validated on replayed Graph answers, and the trials on a real tenant (D4, D5, transcripts, the webhook) are the owner's.
