/// <reference path="../../.orkeon/orkeon.d.ts" />
//
// The engine side of the bridge. `script-host` resolves a crew by name as
// <crews-dir>/<name>/crew.ork.ts, so this file is the crew called "review".
//
// Whatever the command passed as `input` arrives here as `globalThis.inputs`. This crew is
// deliberately LLM-free so the demo runs with no API key; swap the body for a
// `ctx.llm.act(...)` and it becomes a real reviewer.

const inputs = globalThis.inputs ?? {};
// `inputs` values are `unknown`: the JSON is whatever the caller sent. Narrow, then default.
const target = typeof inputs.path === "string" ? inputs.path : "(nothing given)";

const reviewer = agentBuilder()
    .name("reviewer")
    .role("Reviewer")
    .goal("Say something useful about the path it was handed")
    .body((input, ctx) => {
        ctx.log.info(`reviewing ${target}`);
        const verdict = target.endsWith(".cs") || target.endsWith(".ts")
            ? "source file — worth a read"
            : "not a source file — skipped";
        return `${target}: ${verdict}`;
    })
    .build();

const crew = crewBuilder().name("review").withAgent(reviewer).build();

const res = await crew.run();
globalThis.result = res.output;
