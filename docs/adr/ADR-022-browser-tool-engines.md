> 🇫🇷 [Version française](../fr/adr/ADR-022-browser-tool-engines.md)

> **See also**: [ADR-005](./ADR-005-famille-tools-heterogene.md) · [ADR-021](./ADR-021-chat-tool-family.md) · [Back to the index](../INDEX.md)

# ADR-022 — One browser tool whatever the engine: the fourth exception to the scope freeze, widened

**Status**: Accepted · **Date**: 2026-11-24
· **Scope**: the browser tool of `Orkeon.Tools.Web`, its settings section, its engines

This ADR records the decision and the design. The engines are delivered one at a time: the browser guide says which ones are built.

## Context

Orkeon drives one browser, Firefox, through the `firefox` tool. Issues #440 to #444 ask for Chrome, Edge, Brave and Safari. Most of the tool speaks WebDriver BiDi and is not tied to one browser: the actions, the page outline, node ids, tabs, dialogs, the URL policy, the shared instance and the lifetime. What is tied to Firefox is the launch, the endpoint, the profiles and the wording of errors.

The scope is frozen (CONTRIBUTING, "The scope is frozen"); `firefox` is its fourth motivated exception.

## Decision

1. **One tool, `browser`, and no tool per vendor.** It takes the place of `firefox` in the count of built-in tools. The fourth exception is widened from one engine to five (Firefox, Chrome, Edge, Brave, Safari); no engine adds a tool.
2. **The host picks the engine, never the agent.** `Orkeon:Tools:Browser:Engine`, `firefox` by default, one engine per host. An unknown engine is refused when the browser sessions of the host are built; nothing falls back to Firefox. Every answer carries `engine`.
3. **An engine is an internal port.** It finds its executable, creates or opens a profile, refuses a profile another browser holds, removes its own stale disposable folders, launches the browser and hands back a BiDi channel. It declares what it cannot do; an action that needs what it lacks is refused with the reason. Nothing is faked. Every engine takes screenshots.
4. **`firefox` stays as a deprecated second name** of the same tool, over the same session manager: the two names never start two browsers on one profile. The section `Orkeon:Tools:Firefox` is read when `Orkeon:Tools:Browser` is absent; both set is refused. The name, the section and the public `Firefox*` types go at the next major.
5. **Chromium engines run the chromium-bidi mapper themselves**, over the DevTools protocol, with no driver binary. The mapper is embedded at a pinned version with its Apache-2.0 notice.
6. **Safari is measured before any code**: what `safaridriver` covers in BiDi picks its channel.

### Decisions retained (D1–D10)

| # | Question | Retained |
|---|---|---|
| D1 | How long the alias lives | The whole major. |
| D2 | Prefix of the `search` collections | `browser` for every engine: a page embedded under `firefox` is embedded once more. |
| D3 | The scope freeze | The fourth exception is widened to five engines, delivered one at a time. |
| D4 | How one tool answers to two names | The tool is registered twice over one session manager, the second time under the name `firefox`. No alias in `IToolRegistry`. |
| D5 | Public types | The `Firefox*` types keep their names until the next major; `Engine` is added to the options and the answer. |
| D6 | Named user data dirs under a root of Orkeon's | Not at first: a named Chromium profile is a folder path. |
| D7 | The Chromium channel | The mapper run by Orkeon; a driver path only if a measured trial fails. |
| D8 | Where the mapper comes from | The `chromium-bidi` npm package, pinned, its bundle embedded. |
| D9 | CI | Chrome on the Ubuntu runner, Edge on the Windows runner, Safari on the macOS runner; Brave locally. |
| D10 | Safari without BiDi | WebDriver classic behind the same channel. Measured on Safari 27.2 (macOS 26.7.2): `safaridriver` opens a BiDi session, but it has no `input` domain and no screenshot, passes no argument to `script.callFunction`, and sent no event. |

## Alternatives considered

- **One tool per vendor**: crews would name a vendor, and the frozen count would grow by four.
- **An `engine` argument on each action**: a model would choose the browser.
- **Separate Chrome classes beside Firefox's**: two copies of the session logic to keep in step.
- **chromedriver as the BiDi endpoint**: a binary to install, matched to Chrome's major version.

## Consequences

- A crew that lists `firefox` keeps working; new crews list `browser`.
- An engine is validated against the real browser: a machine without it skips that engine's suite, and says so.
