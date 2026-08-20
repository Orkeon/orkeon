# Watch a run from another program (offline demo)

`orkeon run` prints text for a person. `orkeon run --events jsonl` speaks a versioned protocol
instead: one JSON document per line on stdout, one per line on stdin coming back. This folder
holds a reader for it — about 90 lines of Python, no dependencies.

The point is the size. Driving Orkeon from your own tooling should be an afternoon, not a
project.

## Try it with no model configured

```bash
cd examples/run-events
./watch-run.py --replay sample-stream.jsonl
```

`sample-stream.jsonl` is a recorded run: a two-task crew that asks a question halfway through.
Replaying it exercises the whole reader offline.

```
▶  supplier-watch/crew
   ✔ researcher  (7.8s)
   · 1240 tokens
?  Two suppliers changed their prices. Publish the report?
   ✔ writer  (5.7s)
   · 3350 tokens
   ✉ run.progress: {"stage": "reporting"}
✔  done
```

## Point it at a real run

```bash
./watch-run.py ../01-enterprise/01-research-assistant/config.yaml \
  --settings ../appsettings/appsettings.deepseek.local.json
```

Now the question is asked for real and the answer goes back down stdin. Without `--events`, a
task declared `humanInput: true` is **auto-approved** — defensible for an unattended run, and
the wrong answer entirely once something is watching. Asking for the protocol is what makes the
run wait for you.

## What the reader shows about the protocol

Three things in the code are worth copying into your own client.

**Ignore a `kind` you do not know.** A newer Orkeon says more than an older reader understands,
and crashing on an unread line is worse than showing a little less. The `render` function ends
with no `else` on purpose.

**Keep what you could not parse.** A line that is not protocol is still something the run said;
`consume` prints it rather than dropping it. Losing it loses the diagnosis.

**Answer, or the run waits.** `input.needed` carries a `correlationId`; the answer quotes it
back. Replay mode prints the question instead of asking, because there is no run left to receive
an answer — prompting there would only hang.

## The full contract

[The run event bus](../../docs/architecture/run-event-bus.md) — the envelope, every event kind,
every inbound command, and the `client://` seat a watching process gets at the run's own
EventHub, which lets agents write to it and it write back.

Orkeon Studio's Launch screen is the same protocol with a window instead of a terminal.
