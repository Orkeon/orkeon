# RaggableTree showcases

Runnable demos of the RaggableTree semantic code graph (`src/analysis/` — see
the [RaggableTree guide](../../docs/architecture/raggable-tree.md)). Each
sub-example has its own README; `basic-indexing/` and `custom-adapter/` are C#
projects (`run-example.sh` falls back to `dotnet run --project …`), while
`crew-yaml/` is a pure YAML crew for the stock `orkeon` CLI.

| Example | What it shows |
|---------|---------------|
| [`basic-indexing/`](basic-indexing/) | The smallest end-to-end run: index a directory, inspect the 6-level graph |
| [`crew-yaml/`](crew-yaml/) | A YAML-defined crew consuming a RaggableTree index through the analysis tools — pure YAML, run by the stock `orkeon` CLI |
| [`custom-adapter/`](custom-adapter/) | Teaching RaggableTree a new language by implementing `ILanguageAdapter` |
