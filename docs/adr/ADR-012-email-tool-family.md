> 🇫🇷 [Version française](../fr/adr/ADR-012-email-tool-family.md)

> **See also**: [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) · [E-mail tools](../guides/email.md) · [Back to the index](../INDEX.md)

# ADR-012 — The e-mail tool family, a second motivated exception to the scope freeze

**Status**: Accepted · **Date**: 2026-09-26
· **Scope**: `src/tools/Orkeon.Tools.Email`, `Orkeon.Hosting` (the `/credentials` root and the token store), `orkeon email` (`Orkeon.Scripting.Cli`), the `Orkeon.Tools` umbrella

## Context

The owner asked for native tools to read and send mail — SMTP, POP3, IMAP — and for a Gmail
and Hotmail client that reads, writes, creates folders and moves messages into them,
**directly in Orkeon**. What existed was `email_parser`, which read `.eml` files with regular
expressions and returned a placeholder for `.msg`; the tool inventory recorded the gap ("no
tool to send emails").

The scope is frozen: CONTRIBUTING says "no new built-in tool — write yours in a `.ork.ts`
script or a plugin". Neither route fits. A script runs on Jint and cannot open a socket; a
plugin would not be "directly in Orkeon", and would still need the token storage, the rights
and the screening this decision builds.

The provider constraints, checked on 2026-09-26:

- **Hotmail / Outlook.com**: OAuth2 is mandatory and personal accounts get no app passwords.
  Since 2026-09-24, a regression on Microsoft's side makes IMAP OAuth fail for consumer
  accounts ("User is authenticated but not connected"). Microsoft Graph (`Mail.ReadWrite`,
  `Mail.Send`) works for them, accepts MIME in base64 (4 MB per request) and keeps ids stable
  across moves with `Prefer: IdType="ImmutableId"`.
- **Gmail**: an app password (behind 2-Step Verification) still works over IMAP and SMTP, and
  so does OAuth2 XOAUTH2 with `https://mail.google.com/`. Google's device flow refuses Gmail
  scopes, and an application left in "Testing" receives refresh tokens valid for 7 days.

## Decision

1. **A new family, `Orkeon.Tools.Email`, the eighth of the `Orkeon.Tools` umbrella** — the
   second motivated exception to the scope freeze, after the OpenRouter and Mammouth
   aggregators (2026-09-18). Thirteen tools: twelve new, and `email_parser` rebuilt (point 7).
   Dependencies follow ADR-005: `Domain`, `Tools.Abstractions`, `Constants.Configuration`, and
   `Orkeon.Rag` for its prompt-injection detector — `Orkeon.Rag` itself reaches `Application`,
   the route ADR-006's amendment documents; no direct reference to `Application` or
   `Infrastructure`. Everything is `internal` except the DI extensions, the token-store port and
   its file implementation, the error type, the helper the host reads the credentials location
   with, and the administration surface the `orkeon email` verbs use.
2. **MailKit 4.18.0 and MimeKit 4.18.1** (MIT) carry IMAP, POP3, SMTP and the message model.
   Both are referenced directly — central package management pins no transitive version — and
   the umbrella declares them again. There is one message model for every backend: Graph
   messages are read (`$value`) and written as MIME too, so one mapping produces what the tools
   return.
3. **Three backends behind one internal port, chosen by the account's preset.** IMAP + SMTP
   (Gmail and any standard server), POP3 + SMTP (the inbox only; the backend declares its
   capabilities and every request beyond them is refused explicitly, never dropped), and
   Microsoft Graph over plain `HttpClient` — no Graph SDK — for Outlook.com, Hotmail and
   Microsoft 365, the default of the `Outlook` preset. Transport is TLS (`SslOnConnect` or
   `StartTls`); `None` is accepted towards a loopback host only, and no option accepts an
   invalid certificate.
4. **OAuth2 is written by hand**, over `HttpClient`: the RFC 8628 device-code flow for
   Microsoft (tenant `consumers` by default); the authorization code with PKCE for Google, the
   redirect received by a `TcpListener` bound to `127.0.0.1` — not `HttpListener`, which needs a
   URL reservation on Windows — with a paste-the-redirect fallback for WSL, containers and
   remote shells; refresh with token rotation. The tools never sign in interactively: a tool
   without a usable token answers "run `orkeon email login <account>`".
5. **Tokens live in the internal `/credentials` root, through the privileged VFS view.**
   `RunnerVirtualRoots.Credentials` is a new reserved root, refused to a user mount by every
   command. The runner host mounts it as an internal mount only when an OAuth account is
   declared, and `FileSystemEmailTokenStore` writes through `PrivilegedFileSystemAccess`
   (ADR-008), which no agent-facing tool resolves. Physically:
   `<per-user settings directory>/credentials/email/`, or the `email` subdirectory of
   `CredentialsDirectory` for a service. Creating those directories before any mount exists
   (owner-only on Unix) is runner bootstrap — the `Hosting/Runner*` `EXCEPTION-BOOTSTRAP` scope
   already ratified — so there is no new VFS exception category. `IEmailTokenStore` stays
   replaceable by a host of its own (a vault, for instance).
6. **The guard rails are the boundary, not the model's judgment.** The model names an account;
   the operator's configuration holds the servers, the credentials — as environment-variable
   names — and the mandatory `Rights` (`Read`, `Organize`, `Draft`, `Send`, `Delete`, `Purge`).
   Sending fails closed on `Send:AllowedRecipients` (empty: nobody), the SMTP envelope is passed
   explicitly so no `Resent-*` header can widen it, `From` is forced to the account, and
   `MaxRecipients` / `MaxPerHour` cap the volume; `email_draft` is the human-review path.
   Received content is marked untrusted and screened by `PromptInjectionDocumentValidator` on
   the rendered text — flag by default, `Screening:WithholdRejected` to withhold. Every tool
   declares its `ToolAccess` for the permission gate, and `orkeon forge` denies the twelve
   mailbox tools to the crews it forges. Results fit the agent loop's 4000-character cap on a
   tool result, without a new override — mail content is not a trusted deliverable: a search
   page is cut after a whole message and a body slice where its rendering ends, and the
   cursor or the offset resumes exactly there.
7. **`email_parser` joins the family, rebuilt on MimeKit** — same name, parameters `path`,
   `offset` and `max_chars`, the output of `email_read`, `.msg` dropped (it was never parsed).
   This is a deliberate breaking change, with its migration in the CHANGELOG.

## Rejected alternatives

1. **A script or a plugin.** A script cannot open a socket; a plugin is outside Orkeon, which is
   not what was asked, and would have needed the same token storage, rights and screening.
2. **MSAL and the Google SDK.** Two large dependency graphs for three HTTP exchanges each, and
   a token cache that would live outside the VFS.
3. **IMAP for Outlook.com personal accounts.** Broken by Microsoft's regression of 2026-09-24.
   Graph works, keeps ids stable across moves and files sent mail itself; IMAP stays available
   as an option of the `Outlook` preset.
4. **The Gmail REST API.** A second Gmail backend and another consent review, for what IMAP and
   SMTP already cover with an app password or XOAUTH2 — `X-GM-RAW` brings Gmail's own search
   syntax over IMAP.
5. **A new VFS exception category for a plain token file.** The tokens go through the VFS like
   any other file; only the directory's creation is bootstrap, and that is already ratified.

## Consequences

- Built-in tool classes go from 79 to 91 and `Orkeon.Tools` carries eight families. MailKit,
  MimeKit and their BouncyCastle.Cryptography dependency are redistributed inside the `orkeon`
  and `orkeon-repl` tools and the installers ([THIRD-PARTY-NOTICES](../../THIRD-PARTY-NOTICES.md)).
- **Breaking**: `email_parser` leaves `Orkeon.Tools.FileSystem` — `AddOrkeonFileSystemTools()`
  no longer registers it, `AddOrkeonEmailTools(configuration)` does — and its parameters and
  output change.
- **Honest limits**: the token files are plain JSON, shielded from the VFS tools but not from a
  shell or code tool running as the same operating-system user; every account is visible to
  every crew and script that resolves the same settings file; `orkeon-repl` keeps no token
  store, so OAuth accounts work under the `orkeon` runners only. POP3 and Graph limits are in
  the [guide](../guides/email.md#limits).
- **Live validation against real Gmail and Hotmail accounts is the owner's campaign**
  (MAIL-07). Until it is archived, the provider support is documented as campaign-pending.
  *Dated note, 2026-10-06*: Gmail was campaigned on 2026-10-04 (app password) and 2026-10-05
  (OAuth2), and is documented as such; Hotmail and Outlook.com stay campaign-pending. The
  results are in the [guide](../guides/email.md#what-the-gmail-campaign-established).
- Out of this version: deleting folders, copying a message or giving it several Gmail labels,
  Graph uploads above about 3 MB (an upload session), interactive human approval of a send.
