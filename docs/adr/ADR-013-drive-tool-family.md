> 🇫🇷 [Version française](../fr/adr/ADR-013-drive-tool-family.md)

> **See also**: [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) · [ADR-012](./ADR-012-email-tool-family.md) · [Drive tools](../guides/drive.md) · [Back to the index](../INDEX.md)

# ADR-013 — The drive tool family, a third motivated exception to the scope freeze

**Status**: Accepted · **Date**: 2026-10-17
· **Scope**: `src/tools/Orkeon.Tools.Drive`, `Orkeon.Tools.Abstractions/Auth` (the OAuth client the e-mail and drive families share), `Orkeon.Hosting` (the `drive` subdirectory of `/credentials`), `orkeon drive` (`Orkeon.Scripting.Cli`), the `Orkeon.Tools` umbrella

## Context

The owner asked for Google Drive and OneDrive support. Nothing in the repository spoke to either;
a note of the VFS design only envisaged "a network `IFileSystemService`" for remote storage. Two
shapes were possible, and the owner chose the first on 2026-10-17:

- **A tool family**, on the model of the e-mail one: explicit tools an agent calls, each guarded
  by a right.
- **A VFS mount**: a drive mounted under `/drive/...`, every file tool working on it. Deeper, but
  the VFS is synchronous in places, has no notion of a provider's ids, sharing or Google's own
  documents, and latency, quotas and caching would leak into every file tool.

The scope freeze holds for the same reason as for e-mail (ADR-012): a script runs on Jint and
cannot speak OAuth or HTTP to a provider with a stored token; a plugin would not be "directly in
Orkeon" and would rebuild the token storage, the rights and the screening.

The e-mail family already had a hand-written OAuth2 client (device code, loopback PKCE with a
paste fallback, refresh with rotation), a token store behind the internal `/credentials` root,
the secret reading of Studio's user scope, and Microsoft Graph in plain HTTP — all internal to
`Orkeon.Tools.Email`.

## Decision

1. **A ninth family, `Orkeon.Tools.Drive`**, with the layering of ADR-005 (Domain, Tools.Abstractions,
   Orkeon.Rag for the injection detector, Constants.Configuration), held by an allowlist test.
   **No new package**: the Drive API v3 and Microsoft Graph are spoken in plain HTTP with
   `System.Text.Json`.
2. **The OAuth layer moves to `Orkeon.Tools.Abstractions/Auth`**, internal and shared by the two
   families through `InternalsVisibleTo`: the client, the loopback listener, the secret reader and
   the sign-in flows. It throws an `OAuthException`; each family translates it into its own
   exception, same message, matching code. `IEmailLoginInteraction` keeps its name and now
   inherits the public `IOAuthLoginInteraction`, with no member of its own.
3. **Two engines behind one internal port** (`IDrive`), one shape of item. Google Drive is found
   by name under each parent (it has no paths) and its own documents are read and downloaded by
   export; OneDrive is addressed by path.
4. **The operator decides, the agent names an account.** `Rights` (`Read, Write, Organize, Share,
   Delete, Purge`) is mandatory; `Root` confines an account to a folder, an item outside it being
   reported as not found; sharing is closed until `Share:AllowedRecipients` names someone, and
   public links stay off until `Share:AllowPublicLinks`. An account that only reads asks for a
   read-only scope.
5. **The token never leaves the API host.** OneDrive downloads go through the pre-authenticated
   `downloadUrl`, upload sessions get no `Authorization` header, paging links are checked — never a
   redirect followed with the token.
6. **One credentials directory per run.** The drive tokens live in a `drive` subdirectory of the
   e-mail family's `/credentials` root, owner-only on Unix; when the two sections name different
   directories, the e-mail one wins and the runner says so at start.
7. **The forge denies the drive tools**: there is no trial drive, and a forged crew must not touch
   the operator's actual one. `drive_read` screens and marks its text itself, and bypasses the
   result sanitizer like the e-mail tools; the other drive tools stay sanitized.

## Consequences

- 102 built-in tool classes, 49 src and 37 test projects; the umbrella package carries nine
  families.
- `orkeon drive accounts | login | logout | check`; the typings of `.ork.ts` scripts; a "Drive"
  family in Studio's tool list — with no settings tab yet, the requirement names the section.
- Google shared drives, SharePoint libraries, revisions, comments, copies and a VFS mount are out
  of scope, documented in the guide.
- The real-account campaign is the owner's (DRIVE-07): the offline tests script both APIs, and the
  guide says the family is not yet campaigned.
