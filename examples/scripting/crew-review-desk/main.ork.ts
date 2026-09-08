/// <reference orkeon-script="1.0" />
//
// A review desk: three agents, three tasks, a dependency chain, a file on the way out.
//
// DECLARATIVE shape — the last line hands the crew to the runner with
// `globalThis.crew = crew` instead of calling `crew.run()`. That is what makes tasks, the
// process and the manager real; on the procedural shape they are ignored and only `.body()`
// runs. The two shapes are opposites: pick one per file.
//
//   # validate without a key or a model — checks the whole definition loads
//   dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
//       run examples/scripting/crew-review-desk/main.ork.ts --validate
//
//   # a real run needs a provider and a writable /output for the deliverable
//   dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
//       run examples/scripting/crew-review-desk/main.ork.ts \
//       --settings examples/appsettings/appsettings.json \
//       --mount "$PWD/out:/output:rw" --allow-external-mounts

import { pickTools } from "./tools/index.ts";

// ── Agents. role + goal + backstory are not decoration: they are the prompt. ──

const scanner = agentBuilder()
    .name("scanner")
    .role("Change scanner")
    .goal("Establish what a change touches, factually and without judgement")
    .backstory(`Reads diffs for a living. Reports scope — files, size, shape — and refuses to
speculate about intent. Everything downstream depends on this being boring and correct.`)
    // Built-ins, by name. They are resolved from the host catalogue and the resolution is
    // strict: a name that is not registered fails the run rather than being dropped.
    .tools(["file_read", "directory_read"])
    // TypeScript tools, as instances. They travel with the script, so they need no host
    // registration — and no entry in scripts/data/tool-manifest-standard.txt.
    .withAutonomousTools(pickTools("diff_stats", "touched_files"))
    .maxIterations(6)
    .build();

const reviewer = agentBuilder()
    .name("reviewer")
    .role("Code reviewer")
    .goal("Name the risks a change carries, and say which ones matter")
    .backstory(`Ten years of review behind them. Distinguishes a defect from a preference,
and says which is which. Would rather raise two real problems than fifteen nits.`)
    .withAutonomousTools(pickTools("risk_flags"))
    .maxIterations(8)
    .build();

const reporter = agentBuilder()
    .name("reporter")
    .role("Report writer")
    .goal("Turn the scan and the review into something a maintainer reads in a minute")
    .backstory(`Writes the summary that goes on the pull request. Leads with what changed and
what to look at; keeps the evidence underneath, never above.`)
    .maxIterations(4)
    .build();

// ── Tasks. `withContext` is what builds the DAG: a task that declares another as context
//    runs after it and receives its output. ──

const scan = taskBuilder()
    .name("scan")
    .agent(scanner)
    .description(`Read the diff at /script/sample/pr-diff.txt. Report the files touched, the
lines added and removed, and nothing else.`)
    .expectedOutput("A factual scope report: files touched, lines added, lines removed")
    .build();

const review = taskBuilder()
    .name("review")
    .agent(reviewer)
    .description(`Using the scope report, review the same diff. Call risk_flags on it and
judge each flag: is it a real defect here, or noise? Say why.`)
    .expectedOutput("A list of findings, each with a verdict and a one-line justification")
    .withContext(scan)
    .build();

const report = taskBuilder()
    .name("report")
    .agent(reporter)
    .description("Write the pull-request summary from the scope report and the review.")
    .expectedOutput("A short markdown report: what changed, what to look at, what is fine")
    // The deliverable writes the task's final message to the /output mount. Without that
    // mount the run still works and the file simply has nowhere to land.
    .deliverable({ path: "/output/review.md", source: "final_message", format: "markdown" })
    .withContext(review)
    .build();

const crew = crewBuilder()
    .name("review-desk")
    .goal("Review a change and leave a summary a maintainer can act on")
    .process("sequential")
    .withAgents([scanner, reviewer, reporter])
    .withTasks([scan, review, report])
    .verbose(true)
    .build();

// The handoff. Not `await crew.run()` — see the header.
globalThis.crew = crew;
