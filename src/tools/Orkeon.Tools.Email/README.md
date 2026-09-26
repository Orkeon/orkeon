# Orkeon.Tools.Email

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.Email** gives agents a mailbox: `email_accounts`, `email_folders`, `email_search`, `email_read`, `email_save_attachment`, `email_create_folder`, `email_rename_folder`, `email_move`, `email_mark`, `email_delete`, `email_draft`, `email_send`, plus `email_parser` for `.eml` files. IMAP, POP3 and SMTP go through MailKit; Outlook.com and Microsoft 365 go through Microsoft Graph. Gmail and Outlook presets fill in the servers, and OAuth2 accounts sign in once with `orkeon email login`.

Accounts, servers, credentials and rights are the operator's configuration (`Orkeon:Tools:Email`); an agent only names an account. Sending is closed until an allow-list of recipients is declared.

## Install

```
dotnet add package Orkeon.Tools --prerelease
```

> This assembly ships inside the [`Orkeon.Tools`](https://www.nuget.org/packages/Orkeon.Tools) package — the eight built-in tool families in one install; it is not a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [E-mail guide](https://github.com/Orkeon/orkeon/blob/main/docs/guides/email.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
