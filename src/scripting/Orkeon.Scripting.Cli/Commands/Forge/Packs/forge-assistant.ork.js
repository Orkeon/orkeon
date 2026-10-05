/// <reference orkeon-script="1.0" />
// The forge assistant (SPEC-ORKEON-FORGE §7.1): one turn per invocation, stateless by
// design — the engine passes the conversation history in `inputs` and persists it, so a
// resumed session remembers without this script holding anything.
//
// Mechanics adapted from an internal prototype (stable system header for prompt-cache reuse,
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
    lines.push("- Read the request first. Ask a question ONLY when the request does not say");
    lines.push("  what comes in, what comes out, or the shape of what comes out. Whatever the");
    lines.push("  request already says, take it as said and never ask it again.");
    lines.push("- Zero questions is a good result: a request that says what comes in and");
    lines.push("  what comes out goes straight to the submission.");
    lines.push("- Never ask how often the work runs, nor when: scheduling is settled later,");
    lines.push("  outside this conversation.");
    lines.push("- When you do ask, ask ONE question at a time, always with a sensible default");
    lines.push("  the user can accept by saying so. 'I don't know' is always a valid answer:");
    lines.push("  pick the default and move on.");
    lines.push("- The acceptance criteria are the one thing you never leave out: derive 2 to");
    lines.push("  4 verifiable criteria from the request, in the user's own words. They are");
    lines.push("  what the result will be judged against. Ask the user to confirm them only");
    lines.push("  when the request leaves the expected result unclear.");
    lines.push("- Also record: what comes in (with one realistic sample value), what comes");
    lines.push("  out and in which shape, and any constraint the request states (length,");
    lines.push("  tone, language, allowed sources) — from the request when it says them.");
    lines.push("- Folders: list in `folders` every folder the request names OR DESCRIBES for");
    lines.push("  the team, each {path, role, purpose}. path is '/' plus the name as the request");
    lines.push("  spells it (\"/inpdf\"); when the request describes a folder without naming it");
    lines.push("  ('a folder of PDFs with the instructions', 'another with the documents to");
    lines.push("  attach'), name it yourself from what it holds, one short lowercase word in the");
    lines.push("  request's language ('/instructions', '/documents'), one folder per folder");
    lines.push("  described. role is 'input' when the team reads it and 'output' when it writes");
    lines.push("  to it; purpose says in a few words what it holds and how the team must use");
    lines.push("  it, in the user's words — the user may complete it before confirming. When");
    lines.push("  the request names or describes any folder, list one output folder too, with a");
    lines.push("  purpose that means something for this team — when the result is not a file");
    lines.push("  (mails sent, an API called), the output folder is where the record of what was");
    lines.push("  done lands ('/output', 'le compte rendu des envois'). Never invent an INPUT");
    lines.push("  folder the request neither names nor describes; when it names or describes");
    lines.push("  none at all, leave `folders` empty and the defaults are proposed. Never ask");
    lines.push("  about folders: the user confirms the list right after your submission.");
    lines.push("- Always set `readsFiles`: true when the team reads files or documents from a");
    lines.push("  folder (PDFs, spreadsheets, text files it is given), false when what comes in");
    lines.push("  is typed, pasted or fetched from the web (a URL, a text, a question). It");
    lines.push("  decides whether a folder to read is proposed at all.");
    lines.push("- Keep the interview short: when you have goal + acceptance + sample, stop");
    lines.push("  asking and submit.");
    lines.push("- If the submission is rejected, fix exactly what the rejection names and");
    lines.push("  submit again without asking the user anything new.");
    lines.push("- Write in light Markdown at most: **bold** for a label or the one question,");
    lines.push("  '- ' bullets for a short list. No headings, no tables, no code blocks, no");
    lines.push("  links: the user reads you in a chat bubble.");
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

  // The folders the user confirmed (STUDIO-46): the team reads and writes these and nothing
  // else — the trial mounts exactly this list, and so does the adopted team.
  var folders = (input.brief && input.brief.folders) || [];
  if (phase !== "brief" && folders.length > 0) {
    lines.push("");
    lines.push("## The team's folders (confirmed by the user — use these, and only these)");
    lines.push("Each folder comes with the user's own note on what it holds and how the team");
    lines.push("must use it: follow that note to the letter when you design the tasks.");
    for (var f = 0; f < folders.length; f++) {
      var folder = folders[f];
      lines.push("- " + folder.path + " — " + (folder.role === "input" ? "read-only input" : "writable output")
        + (folder.purpose ? ": " + folder.purpose : ""));
    }
    var output = firstOutput(folders);
    if (output) {
      lines.push("Every task deliverable is a path under one of the output folders above");
      lines.push("(\"" + output + "/result.md\"). Agents that read files read the input folders.");
    } else {
      lines.push("No folder is written to (STUDIO-57): the team writes no file, so no task has a");
      lines.push("`deliverable` — its result is what it does (mails sent, an API called, an answer");
      lines.push("given) and what it reports in its final output. Agents that read files read the");
      lines.push("input folders.");
    }
  } else if (phase !== "brief" && input.brief) {
    lines.push("");
    lines.push("## The team's folders");
    lines.push("The user confirmed no folder at all (STUDIO-57): the team reads and writes no file.");
    lines.push("No agent needs a file tool, no task has a `deliverable`; the result is what the team");
    lines.push("does and what it reports in its final output.");
  }

  if (phase !== "brief" && input.crewTools && input.crewTools.length > 0) {
    lines.push("");
    lines.push("## Tools the team may use (closed list — exact names)");
    for (var t = 0; t < input.crewTools.length; t++)
      lines.push("- " + input.crewTools[t].name + ": " + (input.crewTools[t].description || ""));
  }

  // The use case the user started from (STUDIO-40): its team, as a model of structure. Right
  // after the catalogue its tools were cut down to, so everything above it stays the same for
  // every session — and cacheable — whichever reference follows.
  var reference = input.reference;
  if (phase !== "brief" && reference && reference.outline) {
    lines.push("");
    lines.push("## Reference team — a model of STRUCTURE, not content to copy");
    lines.push("The user started from the use case \"" + reference.title + "\" (" + reference.id + ").");
    lines.push("Its team is outlined below. Take its STRUCTURE as a model: how many agents, how the");
    lines.push("work is split into tasks and in which order, the orchestration, which kind of agent");
    lines.push("uses which tool. Fit that structure to the brief — the brief alone decides what the");
    lines.push("team does — and never copy the example's names, wording or domain details. Its tools");
    lines.push("missing from the catalogue above were removed: the catalogue stays the only list.");
    lines.push(reference.outline);
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

// The first output folder of the confirmed list — the example deliverable root; null when
// the team writes nowhere (STUDIO-57).
function firstOutput(folders) {
  for (var i = 0; i < folders.length; i++)
    if (folders[i].role === "output")
      return folders[i].path;
  return null;
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
      parts.push("Open the interview: greet briefly, then ask your first question — or submit straight away when the conversation already says enough.");
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
