> 🇫🇷 [Version française](../fr/guides/firefox.md)

# The `firefox` tool

> **See also**: [Tool inventory](../tools/inventory.md) ·
> [Configuration reference](../reference/configuration.md#orkeontoolsfirefox) ·
> [RaggableTree](../architecture/raggable-tree.md) · [Known limitations](../reference/limitations.md) ·
> [Back to index](../INDEX.md)

`firefox` gives an agent a browser: the Firefox installed on the machine, driven over
[WebDriver BiDi](https://w3c.github.io/webdriver-bidi/) on a WebSocket. No driver binary, no
vendor package, no browser download. The agent opens a page, reads it as an outline of sections,
links and controls, clicks, types, presses keys, searches the page by meaning, runs a script and
saves a screenshot. Pages run their JavaScript: what `web_scrape` cannot see, `firefox` can.

- **One browser per agent run.** Firefox starts at the run's first action, keeps its pages
  from one action to the next, and closes with the run. Agents running side by side have their
  own browsers, or share one with tabs of their own when the host says so (`SharedInstance`).
  Outside any run, an action gets a browser of its own, closed right after it. A host that
  wants the browser to outlive the run keeps it with `Lifetime: host`
  ([below](#a-browser-that-outlives-the-run)).
- **A disposable profile** by default: a fresh folder, removed when the browser closes. The
  host may start the browser on a profile of the Firefox installed on the machine instead, and
  name the profiles an agent may ask for and the ones no browser ever uses
  ([Profiles](#profiles)).
- **The window is visible** by default — the operator sees what the agent does. `Headless`
  hides it; a machine without a display starts headless anyway, with a warning in the log.
- **Every address is checked** by the host's URL policy (`IUrlValidator`), or by the fail-closed
  default guard when the host registers none: private and loopback addresses are refused before
  the browser starts. The address a page lands on after a click, a key or a script is checked
  the same way: a refused one is left for a blank page, and the answer says so.
- **The page is a graph.** A snapshot projects the page into a `RaggableTree` of its own —
  never the code index of the host — and each node carries an id the next actions take.

## Giving it to an agent

```yaml
agents:
  - name: researcher
    tools: [firefox]
```

Firefox must be installed on the host (`firefox` on the PATH, or `ExecutablePath`). The tool is
registered by `AddOrkeonWebTools()`, which every shipped host calls; a library host also needs an
`IFileSystemService` (the screenshot is written through it) and, for `search`,
`AddOrkeonEphemeralSearch()` with an embedding provider configured — without one, every other
action works and `search` says so.

## The actions

One tool, one `action` argument. The permission class of a call depends on the action: the
reading actions pass in `plan` mode, `screenshot` is an edit, the others are executions.

| Action | Arguments | Class | What it does |
|---|---|---|---|
| `navigate` | `url` | Read | Opens an http or https page and returns its outline |
| `back` | — | Read | One page back in the tab's history |
| `snapshot` | `max_nodes`, `offset` | Read | The outline of the page shown: sections, links, controls |
| `read` | `node_id`, `offset` | Read | The text of the page, or of one node and the nodes it holds |
| `find` | `kind`, `name`, `node_id` | Read | The nodes of a kind (`section`, `link`, `control`), whose name holds a text, under a node |
| `search` | `query`, `max_nodes` | Read | The sections closest in meaning to a question (RAG) |
| `screenshot` | `save_to_path` | Edit | Writes the PNG of the visible part of the page to a virtual path |
| `click` | `node_id` | Execute | Clicks an element, scrolled into view first |
| `type` | `node_id`, `text` | Execute | Replaces what a field holds; a line feed presses Enter |
| `press` | `key` | Execute | A key or a chord on the focused element: `Enter`, `Tab`, `Control+a` |
| `evaluate` | `script` | Execute | A JavaScript expression run in the page; a promise is awaited |
| `tabs` | — | Read | The tabs of the agent, in the order they were opened: id, `url`, `title`, `current` |
| `new_tab` | `url` | Read | Opens a tab, on `url` when given, and drives it from now on |
| `switch_tab` | `tab` | Read | Drives the tab `tab` (`t1`, `t2`) from now on, shown in front of the others |
| `close_tab` | `tab` | Execute | Closes a tab, the current one by default; the last tab is never closed (`close` is for that) |
| `close` | `profile` | Execute | Closes the run's browser; the next action starts a new one. Outside any run, under `Lifetime: host`, closes the host's browser of `profile`, the default's without (`profile` counts there only) |

Every answer carries the action, `url` and `title` of the page shown, `tab`, the handle of the
tab the actions drive now, `profile`, the one the browser runs with, and `dialogs`: the
`alert`, `confirm` and `prompt` boxes the page opened during the action, dismissed so the page
never freezes (a `beforeunload` question is accepted: leaving was what the agent asked for).

Every action takes an optional `profile`: it counts at the first action of a run, when the
browser starts; a browser already open keeps its profile, and an action that names another one
is refused until `close`. The rule holds under `Lifetime: host`: the run is bound to the profile
of its first action, and an action without `profile` goes to that browser, whatever the host's
default.

### Tabs

An agent drives one tab at a time and sees only its own: the ones it opened with `new_tab`,
and the ones a link of one of its pages opened — the agent follows such a tab, as a user's eyes
would. A tab the user or another agent opened is never listed, read or closed. `close_tab` on
the tab driven moves the agent to the one before it.

### Node ids

`snapshot`, `find` and `search` name nodes `p<reading>#<index>` — `p2#7` is element 7 of the
second reading of a page. The ids stay valid while the page does not change: typing into a field
then clicking a button needs no new snapshot. After a navigation, or when an action changed the
page, an old id is refused with the reason, and the agent calls `snapshot` again. A listing is
cut at `MaxNodes` (`Orkeon:Tools:Firefox`, 40 by default, 200 at most): `truncated` and
`total_nodes` say so, and `offset` reads the next part.

### A typical flow

```text
navigate  url=https://example.org/docs           → outline: p1#0 section "Getting started" …
search    query="how is a crew configured?"      → hits: p1#5 (0.82) "Configuration" …
read      node_id=p1#5                           → the text of that section
find      kind=control name=search               → p1#12 textbox "Search"
type      node_id=p1#12 text="crew yaml\n"       → the page after Enter, p2#…
click     node_id=p2#3                           → the page the link opened
screenshot save_to_path=/output/docs.png         → saved_path
```

`search` sends the text of each section of the page to the embedding provider as an inline
source of an ephemeral collection named after the page (prefix `firefox`): a page read twice is
embedded once. Each hit names the section's node, so the agent reads or clicks what the search
found: that is the link between the RAG subsystem and the page graph.

## What never goes through the tool

- A **secret**: the text of `type` is a tool argument, which the model writes and the transcript
  keeps. A page that asks for a password is a page the agent should not fill in.
- A **page outside the policy**: the sub-resources a page loads (images, scripts, frames) are
  fetched by Firefox itself and are not checked by the URL policy; only the pages the tab shows
  are. Keep the browser on an isolated network when the policy matters.
- The **text of a page is not screened** for prompt injection: an agent reads it as it reads
  any tool result. The host's Guardian applies to what the agent then says and does.

## Profiles

A browser starts on one of three kinds of profile, and the answer's `profile` says which:

- **`disposable`**, the default: a fresh folder under `ProfileRoot`, with the preferences a
  quiet first run needs, removed when the browser closes. Nothing survives the browser.
- **A profile of the Firefox installed on the host**, by the name `profiles.ini` gives it
  (`about:profiles` shows them): used in place, with its cookies, sessions, extensions and
  history, and never removed. The agent sees what the profile's user sees, signed in where they
  are: give an agent a profile made for it, never the operator's own. A profile another
  Firefox holds open is refused: close that Firefox, or use another profile.
- **A profile folder**, by its path: the same, for a profile `profiles.ini` does not list.

Who chooses: `Orkeon:Tools:Firefox:Profile` names the profile a browser starts on when the
agent names none. An agent may name `disposable`, that profile, or one of `AllowedProfiles` —
nothing else. `DeniedProfiles` wins over all of them: a profile listed there starts no browser,
whoever asks. Names are compared without regard to case.

```json
"Orkeon": { "Tools": { "Firefox": {
  "Profile": "agent-work",
  "AllowedProfiles": ["agent-reviews"],
  "DeniedProfiles": ["default", "default-release"]
} } }
```

## One browser for several runs

By default every agent run has a browser of its own. `SharedInstance: true` gives the host one
browser per profile, shared between the runs that use it: each run drives tabs of its own in it,
sees only those, and the browser closes when the last run leaves it — unless `Lifetime: host`
keeps it ([below](#a-browser-that-outlives-the-run)) — or when the host stops. `close` closes the
tabs of the run that calls it, not the browser the other runs share. A named
profile is what makes sharing useful: several agents on one signed-in session, side by side.

## A browser that outlives the run

`Lifetime: host` keeps the browser once the run ends: its window, its tabs and its profile — the
disposable one included — stay as the last action left them, and the next run, or an action
outside any run, finds them. One browser per profile: an action names the browser it addresses
with `profile`, or gets the host's default. The browser closes when an agent calls `close`,
when the user closes the window, or when the host stops; the disposable folder goes with it. An
action that fails or is cancelled leaves it open. A browser the user closed, or that has died, is
noticed at the next action, which starts a new one. The session is the host's as well: `tabs`
lists the tabs the earlier runs left, and the next run drives the one the last was on.

Runs that overlap on one profile queue their actions on its browser, one at a time: the host's
browser is shared by every run of the host, which fits a console with one operator — an agent
that picks up where the last one left, a page the user reads between two runs — not agents that
browse side by side. For those, `SharedInstance` stays the answer, and the two combine: each run
still sees tabs of its own, which close with the run, and the window stays open for the next one
on a blank tab.

```json
{ "Orkeon": { "Tools": { "Firefox": { "Lifetime": "host" } } } }
```

Inside a run, `close` closes the run's browser, as always — or, when the run has not used one
yet, the host's browser of the profile named, the default's without; outside any run — a console,
a script — `close` closes the host's browser the same way. Each time a disposable profile is
created, the tool first removes the disposable folders under `ProfileRoot` that no Firefox holds
and that are more than ten minutes old: what a host killed with its browsers left behind.

## Settings

`Orkeon:Tools:Firefox` — see the [configuration reference](../reference/configuration.md#orkeontoolsfirefox):
`ExecutablePath`, `Headless`, `Profile`, `AllowedProfiles`, `DeniedProfiles`, `SharedInstance`,
`Lifetime`, `ProfileRoot`, `StartupTimeoutSeconds`, `CommandTimeoutSeconds`, `MaxNodes`,
`MaxTextLength`. When Firefox is a snap, the default profile root is `~/snap/firefox/common/orkeon/profiles`: a
snap cannot read the system's temp folder.
