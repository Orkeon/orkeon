> 🇫🇷 [Version française](SECURITY.fr.md)

# Security Policy

## Supported Versions

| Version | Supported |
|---|---|
| 1.0.x (incl. release candidates) | ✅ |
| ≤ 0.9.x (beta) | ❌ |

Only the latest published release (currently the 1.0.0 release-candidate line) receives security fixes.

## Reporting a Vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report vulnerabilities through [GitHub Private Vulnerability Reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability) on this repository ("Security" tab → "Report a vulnerability").

What to include:

- A description of the vulnerability and its impact.
- Steps to reproduce (proof-of-concept code or configuration if possible).
- The affected component (tool, LLM provider, memory provider, orchestrator…).

We aim to acknowledge reports within **72 hours** and to provide a remediation plan or fix within **30 days** for confirmed issues.

## Threat Model — LLM-Driven Tool Execution

Orkeon is an AI-agent orchestration framework: **LLM output can trigger tool execution**. This makes certain components security-sensitive by design, and you should treat any crew configuration as part of your attack surface:

- **Code/shell execution tools** (`ShellCommandTool`, `SecureCodeInterpreterTool`): commands are allowlist-restricted by default (read-only set) and interpreters (`node`, `dotnet`, `npm`) require an explicit opt-in. There is **no OS-level confinement** unless you route execution through the Docker sandbox (`DockerSandbox`).
- **Network tools** (`WebScrapeTool`, `HttpApiTool`, web search, `rag_ingest` and the opt-in RAG web fallback): URL validation is **fail-closed** — private, loopback, link-local, IPv6-unspecified, multicast, NAT64 and cloud-metadata addresses are denied by default (SSRF protection), and URL ingestion refuses to fetch at all when no `IUrlValidator` is registered (`AddOrkeonInfrastructure` registers one). Redirects are not followed: `AddOrkeonWebTools` gives the web tools their own named `HttpClient` and the RAG page loader its own typed client, both with `AllowAutoRedirect = false`, so a redirect cannot carry a request past the validator that cleared the first hop.
- **File system tools**: all file access goes through the Virtual File System (`IFileSystemService`), validated against configured mounts and access rights.
- **Database tools**: queries pass through `IDatabaseSecurityPolicy` (statement-type allowlist).
- **E-mail tools** (`email_*`): they act on a real mailbox and read mail written by strangers. An agent only names an account; servers, credentials and each account's mandatory `Rights` are operator configuration, and a secret is only ever the *name* of an environment variable. Sending fails closed — `Send:AllowedRecipients` empty means nobody, and the SMTP envelope is the checked recipient list, never read from headers. Every message read is marked untrusted and screened for prompt injection, which flags but can be evaded. **Every account is visible to every crew and `.ork.ts` script that resolves the same settings file** (crews hosted by `orkeon-host` share the host's): declare accounts in the crew's own settings file — not in environment variables or an `appsettings.json` of the directory runs start from, which the .NET host reads for every run — grant the fewest rights, prefer `email_draft` to `email_send`, and never give an untrusted crew both e-mail `Read` and an outbound channel (`http_api`, the web tools). OAuth tokens sit in a file under the per-user settings directory, out of reach of the VFS tools but **not** of a shell or code tool running as the same OS user. Details: [E-mail tools](docs/guides/email.md) and [Security](docs/architecture/security.md#e-mail-tools).
- **Prompt injection**: any content fetched by tools (web pages, files, database rows) may contain adversarial instructions. Run agents with the least-privileged toolset your use case allows.

Vulnerabilities in these layers (allowlist bypass, SSRF filter bypass, VFS escape, sandbox escape, secret leakage in logs, a send outside an account's recipient allow-list) are considered **high severity** — please report them privately.

## Hardening Recommendations

- **`code_interpreter` is never exposed as an `IBaseTool`**: `SecureCodeInterpreterTool` is
  registered as a concrete type only, so no agent can call it unless you expose it yourself.
- **`shell_command` is different — it ships registered.** `AddOrkeonCodeTools()` registers it
  as an `IBaseTool`, and both shipped runners (`orkeon run`, `orkeon-repl`) call that method
  unconditionally, so the tool is in the catalogue out of the box. Its default allowlist is
  **read-only** (`ls`, `cat`, `pwd`, `grep`… plus read-only `git` subcommands); interpreters
  and mutating `git` stay off unless you set `Orkeon:Tools:Shell:AllowInterpreters`, which is
  **RCE-equivalent** and makes the tool emit a security warning. To keep `shell_command` out
  of an agent's reach entirely, compose your host without `AddOrkeonCodeTools()`.
- Use `DockerSandbox` for any code-execution scenario with untrusted input.
- Configure API keys via environment variables or a secret manager — never in YAML crew definitions.
- Enable memory encryption at rest (`EncryptedMemoryProviderDecorator`) for sensitive workloads.

## Verifying What You Install

Every artefact — NuGet packages, installers, CLI archives, `.deb`, MSIs — is built by a
public GitHub Actions workflow and carries a GitHub-signed **build provenance attestation**
(SLSA v1) naming the workflow, the tag and the commit that produced those exact bytes.
NuGet.org publishing goes through Trusted Publishing (OIDC): no long-lived API key exists.
Every release also ships `SHA256SUMS` and, from the next release on, a CycloneDX SBOM
covered by the same attestation.

```bash
gh attestation verify orkeon-cli-<version>-osx-arm64.tar.gz --repo Orkeon/orkeon
```

A package downloaded from nuget.org is repository-signed by nuget.org, which changes its
bytes: strip that signature first (`scripts/nupkg-unsign.py`), then verify. The exact
commands, what each mechanism proves and does not, and what to do when a check fails are in
[Verify what you install](docs/guides/verify-what-you-install.md). An artefact that fails
verification is a security report, not a support question.
