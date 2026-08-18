# Example runners

Host programs used to run the bundled examples. The plain YAML examples need no
runner from here — `orkeon run <config.yaml>` is the standard path (the
`Runner: standard` line in the category READMEs refers to it). The projects in
this directory cover the cases that need more than that:

| Project | Role |
|---------|------|
| [`_shared/`](_shared/) | `Orkeon.Examples.Shared` — library of shared runner plumbing (Terminal.Gui human-input provider, options base) |
| [`trading/`](trading/) | The `03-finance-trading` runner: standard toolset **plus** the 44 `Orkeon.Trading.Tools` classes. `run-example.sh` dispatches to it automatically for that category |
| [`interactive/`](interactive/) | Minimal interactive runner built on `Orkeon.Hosting` (`RunnerHost`) |
| [`interactive-claim-verification/`](interactive-claim-verification/) | Interactive claim-verification session with TypeScript commands — also packaged as the dotnet tool **`orkeon-claim-verify`** |
| [`interactive-interview-spec-forge/`](interactive-interview-spec-forge/) | Interactive interview → specification forge with TypeScript commands — also packaged as the dotnet tool **`orkeon-spec-forge`** |
| [`tui-keytest/`](tui-keytest/) | Terminal.Gui key-capture utility used to debug keybindings in the TUI runners |

Run any of them from a source checkout:

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/31-algo-trading/config.yaml
dotnet run --project examples/runners/interactive
```

The two packaged tools are published with the framework packages (see the
[publication matrix](../../docs/reference/publication-matrix.md)).
