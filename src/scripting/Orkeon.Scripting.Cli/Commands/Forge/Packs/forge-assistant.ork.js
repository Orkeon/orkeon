/// <reference orkeon-script="1.0" />
// The forge assistant (SPEC-ORKEON-FORGE §7.1): one turn per invocation, stateless by
// design — the engine passes the conversation history in `inputs` and persists it, so a
// resumed session remembers without this script holding anything.
//
// Mechanics adapted from experiments/07 (stable system header for prompt-cache reuse,
// volatile user body, capability by tool catalogue); all prompt text here is original.
// The only way this script produces anything is the submit tool the engine injected —
// it never writes a file and never steers the cycle.

"use strict";

var input = globalThis.inputs || {};
var phase = input.phase || "brief";
var language = input.language || "fr";
var readTools = (input.readTools || []).slice().sort();
var submitTool = input.submitTool || "brief_submit";

// ---------------------------------------------------------------------------------------
// Stable header (system message): no timestamps, no counters, sorted tool list — the
// prefix stays byte-identical across the turns of one phase, so providers with prompt
// caching reuse it.
// ---------------------------------------------------------------------------------------
function stableHeader() {
  var lines = [];
  lines.push("You are the Orkeon forge assistant. You help someone who is NOT a developer");
  lines.push("turn a concrete problem into a small team of AI agents. They want their");
  lines.push("problem solved; the team is the means, never the topic. Never mention YAML,");
  lines.push("blueprints, crews or orchestration to the user.");
  lines.push("");
  lines.push("Converse with the user in: " + language + ".");
  lines.push("");

  if (phase === "brief") {
    lines.push("## Your deliverable: the brief");
    lines.push("Interview the user, then call the " + submitTool + " tool with the completed");
    lines.push("brief. Rules of the interview:");
    lines.push("- Ask ONE question at a time, always with a sensible default the user can");
    lines.push("  accept by saying so. 'I don't know' is always a valid answer: pick the");
    lines.push("  default and move on.");
    lines.push("- The acceptance criteria are the one thing you never skip: propose 2 to 4");
    lines.push("  verifiable criteria in the user's own words and have them confirmed. They");
    lines.push("  are what the result will be judged against.");
    lines.push("- Also settle: what comes in (with one realistic sample value), what comes");
    lines.push("  out and in which shape, and any constraint (length, tone, language,");
    lines.push("  allowed sources).");
    lines.push("- Keep the interview short: when you have goal + acceptance + sample, stop");
    lines.push("  asking and submit. Three to five questions is the norm.");
    lines.push("- If the submission is rejected, fix exactly what the rejection names and");
    lines.push("  submit again without asking the user anything new.");
  } else {
    lines.push("## Your deliverable: the team plan");
    lines.push("From the brief below, design the smallest team that satisfies the");
    lines.push("acceptance criteria, then call the " + submitTool + " tool with the full");
    lines.push("plan. Rules of the design:");
    lines.push("- Tools: choose ONLY from the catalogue below, by exact name. Naming any");
    lines.push("  other tool gets the plan rejected.");
    lines.push("- Orchestration: 'sequential' unless the problem truly needs otherwise;");
    lines.push("  the rationale must say why in plain words the user understands.");
    lines.push("- Keys: short kebab-case, unique; every task names an existing agent key;");
    lines.push("  dependencies name existing task keys.");
    lines.push("- crew.name is a short display name (3 to 5 words, 60 characters max) —");
    lines.push("  never the goal sentence. It becomes the team's folder name.");
    lines.push("- Small is right: two or three agents solve most problems. One clear role");
    lines.push("  and goal per agent; tasks phrased as work, not as prompts.");
    lines.push("- If errors are listed below, fix exactly those — change nothing else —");
    lines.push("  and submit again. Do not converse in this phase.");
  }

  if (phase !== "brief" && input.crewTools && input.crewTools.length > 0) {
    lines.push("");
    lines.push("## Tools the team may use (closed list — exact names)");
    for (var t = 0; t < input.crewTools.length; t++)
      lines.push("- " + input.crewTools[t].name + ": " + (input.crewTools[t].description || ""));
  }

  lines.push("");
  lines.push("## Your own tools");
  var catalogue = readTools.concat([submitTool]).sort();
  for (var i = 0; i < catalogue.length; i++)
    lines.push("- " + catalogue[i]);

  // Every path is absolute in virtual space; there is no working directory to be relative
  // to. Without this the model guesses "." on its first read, the mount registry refuses
  // it, and a whole round trip is spent learning what one sentence could have said.
  lines.push("");
  lines.push("Paths are absolute and start at a mount. There is no current directory:");
  lines.push("- /workspace — the user's project, read-only");
  lines.push("- /forge — this session's own folder, writable");
  lines.push("- /output — where deliverables go, writable");
  lines.push("A relative path such as \".\" or \"src\" is refused.");

  return lines.join("\n");
}

// ---------------------------------------------------------------------------------------
// Volatile body (user message): history, context, then the task — last, where attention
// is strongest.
// ---------------------------------------------------------------------------------------
function volatileBody() {
  var parts = [];

  var history = input.history || [];
  if (history.length > 0) {
    parts.push("## Conversation so far");
    for (var i = 0; i < history.length; i++)
      parts.push((history[i].role === "user" ? "User: " : "Assistant: ") + history[i].text);
    parts.push("");
  }

  if (input.brief) {
    parts.push("## The accepted brief");
    parts.push(JSON.stringify(input.brief));
    parts.push("");
  }

  if (input.previousBlueprint) {
    parts.push("## Your previous plan");
    parts.push(JSON.stringify(input.previousBlueprint));
    parts.push("");
  }

  if (input.errors && input.errors.length > 0) {
    parts.push("## Errors to fix, verbatim from the validator");
    for (var j = 0; j < input.errors.length; j++)
      parts.push("- " + input.errors[j]);
    parts.push("");
  }

  parts.push("## Now");
  if (phase === "brief") {
    if (input.message)
      parts.push("The user says: " + input.message);
    else if (input.errors && input.errors.length > 0)
      parts.push("Fix the submission and call " + submitTool + " again.");
    else
      parts.push("Open the interview: greet briefly and ask your first question.");
  } else {
    parts.push("Design the team and call " + submitTool + ".");
  }

  return parts.join("\n");
}

// ---------------------------------------------------------------------------------------
// One turn: an agent whose body is a single act() pass over the phase's catalogue.
// ---------------------------------------------------------------------------------------
var catalogue = readTools.concat([submitTool]);

var assistant = agentBuilder()
  .name("forge-assistant")
  .role("Forge assistant")
  .goal("Turn a concrete problem into a deployable agent team")
  .tools(catalogue)
  .maxIterations(8)
  .body(async function (_ignored, ctx) {
    var result = await ctx.llm.act(volatileBody(), { system: stableHeader(), maxIterations: 8 });
    return (result && result.output) || "";
  })
  .build();

globalThis.crew = crewBuilder()
  .name("forge-assistant")
  .goal("One forge turn")
  .process("autonomous")
  .budget({ toolCalls: 24, wallTime: 600, tokens: 300000, delegationDepth: 1 })
  .withAgent(assistant)
  .build();
