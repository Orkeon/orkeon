# Quickstart — one agent, one writable mount, a local model

The crew behind the README's *Try it in two minutes*. One agent (`Scribe`), one tool
(`file_write`), one task: write three lines to `/output/hello.md`. Its `appsettings.json`
points at Ollama on `localhost:11434` with `qwen2.5:1.5b`, so nothing else has to be
configured — the settings file next to the crew wins over every other source.

```bash
ollama pull qwen2.5:1.5b
dotnet tool install -g Orkeon.Scripting.Cli --prerelease
mkdir -p out && orkeon run examples/quickstart/crew.yaml --mount ./out:/output:rw
```

`./out/hello.md` appears. That is the whole demo — except for what it demonstrates: the
agent never saw `./out`. It saw `/output`, a virtual path, the only mount of this run,
mounted `rw`. Ask it to write anywhere else and the file-system service answers before
any byte reaches the disk (measured with this very crew, path changed to `/etc/hello.md`):

```
warn: FileSystemService  Access denied by registry for virtual path '/etc/hello.md' requiring Write
Tool result << file_write [FAIL]: Error: No mount found for virtual path '/etc/hello.md'.
                                  Available mounts: /output (writable), /crew (read-only)
```

Try it: edit `crew.yaml`, replace both `/output/hello.md` with `/etc/hello.md`, run again.

## Why this model

Because the boundary is the point, not the prose. `qwen2.5:1.5b` is 986 MB, runs on any
laptop CPU in seconds, and calls a tool the way a tool is called — measured five runs out
of five under Ollama 0.34.0 and three out of three under 0.12.3. The README first shipped
with `llama3.2:1b`, which is 1.3 GB and one size smaller in judgement: under Ollama 0.12.3
it made a real tool call with the whole envelope inside the arguments (the dispatcher
unwraps that now), and under 0.34.0 — the version pinned in CI — it wrote the envelope as
its answer, with a broken JSON string, every single time, so no tool ran. Two runtime
guards came out of that run (a JSON envelope in the text is parsed as a tool call; an
answer shaped like a tool call that cannot be executed is handed back to the model instead
of accepted as final), and the model changed. Any bigger model works too:
`orkeon init --provider ollama --model <name>` or edit `appsettings.json`.

## Verified by CI

`.github/workflows/quickstart.yml` extracts the fenced block between the
`<!-- quickstart:begin -->` / `<!-- quickstart:end -->` markers of `README.md`, checks that
`README.fr.md` carries the identical block, and runs it against a pinned Ollama binary on a
GitHub runner. The README is the test; a README that stops working turns the build red.
