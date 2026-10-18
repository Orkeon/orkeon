# Orkeon.Tools.Drive

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.Drive** gives agents a cloud drive: `drive_accounts`, `drive_list`, `drive_search`, `drive_info`, `drive_read`, `drive_download`, `drive_upload`, `drive_create_folder`, `drive_move`, `drive_delete` and `drive_share`. Google Drive goes through the Drive API v3, OneDrive (personal or for work) through Microsoft Graph, both over plain HTTP with no vendor SDK. Accounts sign in once with OAuth2 (`orkeon drive login`).

Accounts, clients, rights and the root folder an agent is confined to are the operator's configuration (`Orkeon:Tools:Drive`); an agent only names an account. Sharing is closed until an allow-list of recipients is declared, and public links stay off unless an operator allows them.

## Install

```
dotnet add package Orkeon.Tools --prerelease
```

> This assembly ships inside the [`Orkeon.Tools`](https://www.nuget.org/packages/Orkeon.Tools) package — the nine built-in tool families in one install; it is not a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Drive guide](https://github.com/Orkeon/orkeon/blob/main/docs/guides/drive.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
