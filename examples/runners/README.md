# Example runners

Host programs used to run the bundled examples. The plain YAML examples need no
runner from here — `orkeon run <config.yaml>` is the standard path (the
`Runner: standard` line in the category READMEs refers to it). The projects in
this directory cover the cases that need more than that:

| Project | Role |
|---------|------|
| [`_shared/`](_shared/) | `Orkeon.Examples.Shared` — library of shared runner plumbing (Terminal.Gui human-input provider, options base) |
| [`trading/`](trading/) | The `03-finance-trading` runner: standard toolset **plus** the 44 `Orkeon.Trading.Tools` classes. `run-example.sh` dispatches to it automatically for that category |

Run any of them from a source checkout:

```bash
dotnet run --project examples/runners/trading -- --config examples/03-finance-trading/31-algo-trading/config.yaml
```
