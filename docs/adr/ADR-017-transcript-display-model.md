> 🇫🇷 [Version française](../fr/adr/ADR-017-transcript-display-model.md)

> **See also**: [TypeScript commands](../architecture/cli-ts-commands.md) · [Back to the index](../INDEX.md)

# ADR-017 — The transcript paints over its text, and only content paints a background

**Status**: Accepted · **Date**: 2026-11-05
· **Scope**: `Orkeon.Cli.TerminalGui` (`TranscriptBuffer`, `TranscriptView`, `SchemeFactory`), `Orkeon.Cli.Abstractions` (`IBlockConsole`), `Orkeon.Cli.Commands.Scripting` (`ctx.writeBlock`)

## Context

The split-pane transcript stored plain text, so nothing written in it could carry a colour: not a
submitted prompt, not a diff, not rendered markdown. The layout also had a rule, every attribute keeps
the terminal's background and a line holds one vivid role, so it never fights the user's theme. A diff
needs backgrounds, and a script needs a way to ask for one without knowing about colours.

## Decision

1. **The text of a line stays the one source** for the wrap, the positions, the selection and the trim.
   A styled line adds sorted runs, a role painted over its whole row, a hanging indent and an optional
   copy source. Nothing of this reaches the wrap except the indent.
2. **Only content paints a background**: a submitted prompt, a diff line, a changed word. The layout
   around it keeps the terminal's background. A role with a background always sets its foreground too,
   so it reads the same on a dark and on a light terminal, without knowing which one it runs on.
3. **A copy of a whole line returns its source** when it has one; a copy of a part returns what is
   shown. Mapping every displayed char back to its source is not worth its cost.
4. **Scripts hand over a source and a kind, never a colour.** `ctx.writeBlock({ kind, text })` reaches a
   console that implements `IBlockConsole`, and any other console writes `text` as plain lines. The
   styled model stays internal to `Orkeon.Cli.TerminalGui`: the rendering happens there, so the runs
   never cross a public interface and scripts cannot depend on them.
5. **Styled lines take the empty line left open after a final line break and open a new one**, so they
   add no blank row and stream text written next never extends them.

## Consequences

- Plain and replay output do not change: `--ui plain` shows the same source as before.
- Rendering a block costs the renderer's time on the UI thread, for each block written.
- A partial selection across a rendered line copies the rendered text, not the source.
- Palettes came after this decision: `Orkeon:Cli:Tui:Theme` and `ctx.setTheme` name the one in use (`Dark`,
  `Light`, or `None`, the terminal's own colours), and `SchemeFactory` reads each role in it. The palette is
  named, never detected, so decisions 2 and 4 hold: a script still hands over no colour.

## Alternatives rejected

- **ANSI sequences in the text.** The wrap, the positions and the selection would all have to skip them,
  and a script could then print colours that plain output shows as garbage.
- **A public styled-run type for scripts.** It would freeze the display model into the public API and
  make every script choose colours.
