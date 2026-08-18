> 🇫🇷 [Version française](../fr/guides/local-models.md)

# Run Orkeon on local models

> **See also**: [Three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md) · the `examples/appsettings/` profiles · [Back to the index](../INDEX.md)

Every Orkeon example and crew can run against a model on your own machine — no
API key, no cloud. There are three ways to do it, from zero-install to fully
self-contained:

| Option | Where the model runs | Best for |
|---|---|---|
| **Docker Model Runner** (DMR) | on the host, port `12434` | Docker Desktop users — the containers' default settings already point at it |
| **Ollama** | on the host, port `11434` | existing Ollama setups |
| **Embedded** (`--target local-llm`) | inside the `orkeon-runners` image | zero host-side setup, air-gapped demos |

The `orkeon-runners` container image defaults to option 1: its baked settings
target `host.docker.internal:12434` with the `ai/granite-4.0-h-tiny` model.

## Option 1 — Docker Model Runner (recommended with Docker Desktop)

```bash
# 1. Pull a model from the Docker catalog (once, on the HOST):
docker model pull ai/granite-4.0-h-tiny

# 2. Verify it is fully downloaded — an interrupted pull leaves NO model:
docker model list

# 3. Run the examples — the default settings just work:
docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD/out:/output" \
  ghcr.io/orkeon/orkeon-runners
# then inside the shell:
orkeon-example run 1
```

**Pitfalls we hit so you don't have to:**

- **Catalog tags**: `docker model pull` resolves against the Docker Hub `ai/`
  catalog. A tag that is not in the catalog fails with
  `404 Not Found: Model not found` — e.g. `ai/gemma4` exists, a made-up
  `ai/gemma4:128K` does not. Check https://hub.docker.com/u/ai or
  `docker model list` for what you actually have.
- **Interrupted pulls**: a cancelled `docker model pull` leaves nothing behind,
  and `docker model configure` on a missing model **fails silently**. Always
  confirm with `docker model list` before configuring.
- **Model ids**: DMR announces models under their fully-qualified id
  (`docker.io/ai/granite-4.0-h-tiny:latest`). Orkeon's settings can use the
  short form (`ai/granite-4.0-h-tiny`) — the container preflight matches by
  substring and the server accepts both.

### Switching models

```bash
# run any pulled model without touching settings files:
docker run -it --rm -e ORKEON_RUNNER=shell \
  -e ORKEON_Llm__Model=gemma4 \
  ghcr.io/orkeon/orkeon-runners
```

`ORKEON_Llm__*` environment variables override every settings file (they are
.NET configuration overrides), so `-e ORKEON_Llm__Model=…` composes with any
profile. `docker model list` on the host shows the names you can use.

### Bigger context windows

DMR serves each model with its default context size. Raising it (e.g. to 128K
for `gemma4`) is a per-model runtime configuration — but the tooling around it
is deceptively quiet, so follow the procedure below and **always verify with
`configure show`**, never with `docker model list` or `inspect` (both only show
*packaging* metadata; the `CONTEXT` column stays empty even when the runtime
configuration is applied, and `configure` itself prints nothing on success or
failure alike).

**Step 1 — try the direct configuration:**

```bash
docker model configure --context-size 131072 gemma4
docker model configure show gemma4
```

If `configure show` prints the setting, you are done:

```json
[
  {
    "Backend": "llama.cpp",
    "Model": "docker.io/ai/gemma4:latest",
    "Mode": "completion",
    "Config": { "context-size": 131072 }
  }
]
```

**Step 2 — if `configure show` shows nothing (or errors): go through
`docker model tag`.** Tagging duplicates an installed model instantly (no
re-download) and gives you a reference the configuration reliably attaches to.
Full walkthrough with `gemma4`:

```bash
# 1. the model must be fully pulled first — an interrupted pull leaves nothing,
#    and configure/tag are silent or fail on a missing model
docker model list                     # gemma4 must appear with its size

# 2. duplicate it under a dedicated large-context alias
docker model tag gemma4 gemma4:128K
#    -> Model "gemma4" tagged successfully with "gemma4:128K"

# 3. configure the alias
docker model configure --context-size 131072 gemma4:128K

# 4. VERIFY — the only command that shows the runtime config
docker model configure show gemma4:128K
#    -> "Config": { "context-size": 131072 }
```

Then run your crews with the model name:

```bash
docker run -it --rm -e ORKEON_RUNNER=shell \
  -e ORKEON_Llm__Model=gemma4 \
  -v "$PWD/out:/output" ghcr.io/orkeon/orkeon-runners
```

Note (observed on Docker Desktop, July 2026): `configure show` reports the
configuration against the canonical reference (`docker.io/ai/gemma4:latest`) —
it is keyed by the model **ID**, which both tags share, so the setting applies
to every tag of the same model; `-e ORKEON_Llm__Model=gemma4` picks it up.

**Step 3 — ground truth (optional but definitive):** ask llama.cpp itself.
Trigger a first request (the model loads lazily), then read the served `n_ctx`:

```bash
curl -s -X POST http://host.docker.internal:12434/engines/llama.cpp/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"gemma4","messages":[{"role":"user","content":"hi"}],"max_tokens":1}' > /dev/null
curl -s http://host.docker.internal:12434/engines/llama.cpp/v1/models | grep -o '"n_ctx[^,]*'
```

(from inside a container; on the host use `localhost:12434`.)

If your Docker Desktop version lacks these commands entirely, use the Desktop
UI (Models → model → settings) or the **embedded variant** with
`--build-arg LOCAL_MODEL_CTX=131072` (option 3 below), where the context size
is fully under your control.

Two things to keep straight:

- The **context size** is a server-side property (how much the model can read);
  a 128K KV cache costs several extra GB of host RAM.
- `Llm.MaxTokens` in the Orkeon settings caps the **response** length only —
  it is independent of the context size.

### Concurrency: keep `MaxConcurrentRequests` at 1

A local inference server saturates the machine with a **single** request; two
agents firing in parallel double the KV-cache memory and thrash the CPU/GPU
instead of speeding anything up. Every local profile Orkeon ships therefore
pins the rate limiter:

```json
"RateLimiting": { "MaxConcurrentRequests": 1 }
```

If you write your own settings file, keep that block — **absent or `0` means
UNLIMITED concurrency** (the limiter only engages for values `> 0`), and
parallel or consensual crews will happily open one connection per agent.
Raise it only for cloud providers (the shipped cloud templates use 2–16).

## Option 2 — Ollama on the host

```bash
ollama pull llama3.2          # on the host
docker run -it --rm -e ORKEON_RUNNER=shell \
  -e ORKEON_LLM_PROFILE=host-ollama \
  ghcr.io/orkeon/orkeon-runners
```

The `host-ollama` profile targets `http://host.docker.internal:11434` (the
`11434` port routes to Orkeon's native Ollama provider). Override the model
with `-e ORKEON_Llm__Model=<name>` if you pulled something else.

On a plain Linux engine (no Docker Desktop), `host.docker.internal` does not
exist — add `--add-host=host.docker.internal:host-gateway` to `docker run`.

## Option 3 — Embed the model in the image

Build a variant that needs no host-side server at all: llama.cpp's
`llama-server` plus one GGUF are baked in and served inside the container on
the same URL shape as DMR, so the settings need zero changes. Recipes live at
the `local-llm` stage of `Dockerfile.runners`:

```bash
# Granite 4.0 h-tiny (Apache 2.0, ~4.2 GB of weights → ~6 GB image)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/ibm-granite/granite-4.0-h-tiny-GGUF/resolve/main/granite-4.0-h-tiny-Q4_K_M.gguf \
  -t orkeon-runners:granite .

# Gemma 4 E4B with a 128K context (Gemma license — keep the image local)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/unsloth/gemma-4-E4B-it-qat-GGUF/resolve/main/gemma-4-E4B-it-qat-UD-Q4_K_XL.gguf \
  --build-arg LOCAL_MODEL_NAME=ai/gemma4 \
  --build-arg LOCAL_MODEL_CTX=131072 \
  -t orkeon-runners:gemma4 .

docker run -it --rm -m 8g -e ORKEON_RUNNER=shell orkeon-runners:granite
```

- `LOCAL_MODEL_CTX` bakes the default context size (8192 if omitted); override
  per run with `-e ORKEON_LOCAL_LLM_CTX=…`.
- `-e ORKEON_LOCAL_LLM=0` starts the container without the embedded server.
- CPU inference: expect roughly 5–15 tokens/s; give the container memory
  (`-m 8g`, more at large context sizes — on WSL2 raise `.wslconfig`).
- These variants are build-your-own **by design**: no pre-built tag is
  published, so model-license obligations (notably Gemma's) stay on your side.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `docker model pull …: 404 Not Found` | tag not in the `ai/` catalog | check the catalog / `docker model list`; plain `ai/gemma4` vs invented tags |
| `orkeon-example: cannot reach the LLM endpoint` | no server on the host / Linux engine | start DMR or Ollama; on Linux add `--add-host=host.docker.internal:host-gateway` |
| `endpoint … is up, but it does not serve the configured model` | model not pulled (or name mismatch) | the message prints the exact `docker model pull` and the models actually served |
| `LLM API Error (NotFound): model not found` mid-crew | preflight skipped (`ORKEON_SKIP_PREFLIGHT=1` or direct `orkeon run`) | same fixes as above |
| `docker model configure` seems to do nothing | configure is always silent; the model may be missing, or your Desktop version may ignore the flag | `docker model list` first; then verify with the served `n_ctx` check above — not with the `CONTEXT` column |
| crew "succeeds" but invents content unrelated to the bundled documents | the example's `data/` was not mounted | use `orkeon-example run` (mounts it automatically) instead of a bare `orkeon run` |
| `Access denied … Mounts granting Write: /output` | the model wrote outside `/output` | that message lets it self-correct on the next iteration; only `/output` is writable |
| very slow / OOM at 128K context | KV cache memory | lower the context, or raise container/VM memory |
