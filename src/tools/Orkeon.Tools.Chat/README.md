# Orkeon.Tools.Chat

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.Chat** gives agents the chat their team uses: `chat_accounts`, `chat_list`, `chat_read`, `chat_members` and `chat_send`. Microsoft Teams goes through Microsoft Graph over plain HTTP with no vendor SDK, as the signed-in user. Accounts sign in once with OAuth2 (`orkeon chat login`).

Accounts, rights and the places an agent may read or post in are the operator's configuration (`Orkeon:Tools:Chat`); an agent only names an account. Reading and posting are closed until places are allowed.

## Install

```
dotnet add package Orkeon.Tools
```

> This assembly ships inside the [`Orkeon.Tools`](https://www.nuget.org/packages/Orkeon.Tools) package — the ten built-in tool families in one install; it is not a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Chat guide](https://github.com/Orkeon/orkeon/blob/main/docs/guides/chat.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
