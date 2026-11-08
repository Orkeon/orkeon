> 🇫🇷 [Version française](../fr/guides/testing.md)

# Record and replay LLM calls

`--llm-record` freezes what a real run sent to its models and what they answered; `--llm-replay` plays
that run again from the file, with no network call and no model. A crew test then reads a real answer
without paying for it twice, and a regression shows as a replay miss instead of a changed bill.

For answers written by hand, use [`--llm-mock`](../reference/cli.md#orkeon-run) instead. The three
switches are exclusive two by two (exit `1`, one line).

## Record, then replay

```bash
orkeon run crew.yaml --llm-record cassettes/report.jsonl     # real provider, every call written
orkeon run crew.yaml --llm-replay cassettes/report.jsonl     # same output, no network
```

The same two switches exist on a procedural script (`orkeon run script.ork.ts`) and on `orkeon eval run --crew`. A replay counts as offline: `--offline --llm-replay` runs on any profile. The cassette's folder is
mounted at `/llm-cassette` (read-write to record, read-only to replay) and may hold neither the crew nor the
script. The file is written through the virtual file system like every other file the framework touches;
a cassette outside the working directory needs `--allow-external-mounts`.

A host that does not use the CLI sets the same thing in its settings: `Llm:Replay:Mode` (`off`, `record`,
`replay`) and `Llm:Replay:Cassette` (a virtual path), see [configuration](../reference/configuration.md).

## What a cassette holds

One JSONL line per call: the profile that made it, its key, its occurrence number, the answer (content,
token counts, model, metadata, raw body) and, for a streamed call, the list of deltas, tool-call fragments
included. The decorator sits in the provider factory, under the call-mode layer, on the vendor's provider:
the cassette holds the messages that really went over the wire, including the prompt-based tool-calling
emulation of a model without native tools.

The **key** is the SHA-256 of the profile, the messages (role, content, tool call ids), the tools and the
response format. Timestamps (`2026-11-08T10:11:12Z`) and generated identifiers (GUIDs) in the text are
replaced by a fixed mark first, so a second run asks for the same key. A call repeated with the same key
gets an occurrence number: the n-th identical call replays the n-th recorded answer. Identical calls made
at the same moment are served in the order they arrive.

The sampling options and the model name (`temperature`, `max_tokens`, `model`, the seed, the timeout) are **not**
part of the key: changing them does not miss, and a replay answers with what was recorded under the old ones.
Only the profile, the messages, the tools and the response format count.

**Secrets**: before a line is written, these are replaced by `[REDACTED]`: the API key the provider was
configured with, a `Bearer` token, a vendor key shaped `sk-...`, a Google key shaped `AIza...`, the value of a
`key=`, `api_key=`, `apikey=`, `token=` or `access_token=` query parameter of a URL, and the user info of a
URL (`https://user:password@host`). The request itself is never stored, only its key. Nothing else is
redacted: the answers are stored as they came, and a model may repeat what it was given. Check a cassette
before committing it.

## A replay miss

A call the cassette does not hold (another prompt, another tool list, one call more than recorded) fails
with `ReplayMissException`: the message names the cassette, the profile, the call number and the start of the
prompt. A replay never falls back on the network, and a cassette that does not exist is refused before the run.

## What it does not do

- Tools run again on a replay: only the model's answers are frozen. A tool with side effects does them twice.
- Identical calls that two agents make at the same moment take the recorded answers in the order they arrive, so a run whose parallel identical calls change order may swap them.
- Embeddings and memory are not LLM calls and are not recorded.
- Changing a prompt, a tool or the response format changes the key: record the cassette again.
