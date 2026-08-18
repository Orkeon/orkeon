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
- **Network tools** (`WebScrapeTool`, `HttpApiTool`, web search): URL validation is **fail-closed** — private, loopback, link-local, and cloud-metadata addresses are denied by default (SSRF protection). Redirects are not followed automatically.
- **File system tools**: all file access goes through the Virtual File System (`IFileSystemService`), validated against configured mounts and access rights.
- **Database tools**: queries pass through `IDatabaseSecurityPolicy` (statement-type allowlist).
- **Prompt injection**: any content fetched by tools (web pages, files, database rows) may contain adversarial instructions. Run agents with the least-privileged toolset your use case allows.

Vulnerabilities in these layers (allowlist bypass, SSRF filter bypass, VFS escape, sandbox escape, secret leakage in logs) are considered **high severity** — please report them privately.

## Hardening Recommendations

- Keep dangerous tools (shell, code interpreter) **unregistered** unless required; they are not exposed as `IBaseTool` by default.
- Use `DockerSandbox` for any code-execution scenario with untrusted input.
- Configure API keys via environment variables or a secret manager — never in YAML crew definitions.
- Enable memory encryption at rest (`EncryptedMemoryProviderDecorator`) for sensitive workloads.
