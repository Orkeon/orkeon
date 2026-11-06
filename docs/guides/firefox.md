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

- **One browser per agent run.** Firefox starts at the run's first action, in a profile folder
  of its own, keeps its page from one action to the next, and closes with the run; the profile
  is removed then. Agents running side by side have their own browsers. Outside any run, an
  action gets a browser of its own, closed right after it.
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
| `close` | — | Execute | Closes the run's browser; the next action starts a new one |

Every answer carries the action, `url` and `title` of the page shown, and `dialogs`: the
`alert`, `confirm` and `prompt` boxes the page opened during the action, dismissed so the page
never freezes (a `beforeunload` question is accepted: leaving was what the agent asked for).

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

## Settings

`Orkeon:Tools:Firefox` — see the [configuration reference](../reference/configuration.md#orkeontoolsfirefox):
`ExecutablePath`, `Headless`, `ProfileRoot`, `StartupTimeoutSeconds`, `CommandTimeoutSeconds`,
`MaxNodes`, `MaxTextLength`. When Firefox is a snap, the default profile root is
`~/snap/firefox/common/orkeon/profiles`: a snap cannot read the system's temp folder.
