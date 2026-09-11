# Quickstart — one agent, one writable mount, a local model

The crew behind the README's *Try it in two minutes*. One agent (`Scribe`), one tool
(`file_write`), one task: write three lines to `/output/hello.md`. Its `appsettings.json`
points at Ollama on `localhost:11434` with `llama3.2:1b`, so nothing else has to be
configured — the settings file next to the crew wins over every other source.

```bash
ollama pull llama3.2:1b
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

## Why a 1B model

Because the boundary is the point, not the prose. `llama3.2:1b` is 1.3 GB, runs on any
laptop CPU in seconds, and is small enough to make mistakes — its first attempt wraps the
whole tool-call envelope inside the arguments (`{"type":"function","function":"file_write",
"parameters":{…}}`), which the dispatcher now unwraps. Any bigger model works too:
`orkeon init --provider ollama --model <name>` or edit `appsettings.json`.

## Verified by CI

`.github/workflows/quickstart.yml` extracts the fenced block between the
`<!-- quickstart:begin -->` / `<!-- quickstart:end -->` markers of `README.md`, checks that
`README.fr.md` carries the identical block, and runs it against a pinned Ollama binary on a
GitHub runner. The README is the test; a README that stops working turns the build red.
