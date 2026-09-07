/// <reference path="./.orkeon/orkeon-cli.d.ts" />
//
// The `script-host` façade: a command launches a CREW, where dispatch.cmd.ts addresses an
// AGENT. Two different services, and the difference is the whole point of the split between
// a control plane and an engine:
//
//   ctx.services.get("commands")     → request/post to a live agent by name
//   ctx.services.get("script-host")  → run <crews-dir>/<name>/crew.ork.ts
//
// Point the runner at both directories:
//
//   dotnet run --project src/apps/Orkeon.ConsoleApp -- --runner=scripted-commands \
//       --commands-dir examples/cli-ts-commands \
//       --crews-dir    examples/cli-ts-commands/crews
//
//   scripted> crews                       # → review
//   scripted> review src/Program.cs       # → src/Program.cs: source file — worth a read
//   scripted> review-bg src/Program.cs    # → launched (ticket t1), result replayed later

// ── What crews the host found. `--crews-dir` is repeatable; this lists them all. ──
defineCommand({
  name: "crews",
  description: "List the crews the host discovered.",
  handler: function (args, ctx) {
    const names = ctx.services.get("script-host").listCrews();
    return ctx.continue(names.length ? names.join(", ") : "(no crews — pass --crews-dir)");
  },
});

// ── Sync: block until the crew is done. Right for a crew measured in seconds. ──
defineCommand({
  name: "review",
  description: "Run the 'review' crew on a path and wait for it.",
  args: { path: { type: "string", required: true } },
  handler: function (args, ctx) {
    const out = ctx.services.get("script-host").runCrew("review", { path: args.path });
    return out.ok
      ? ctx.continue(out.summary)
      : ctx.continue("crew failed: " + out.error);
  },
});

// ── Async: same crew, ticket returned immediately, `completed` replays at the next pump.
//    The ticket cycle is the one `commands.post` uses — `ps`, `inspect`, `result`, `cancel`
//    in dispatch.cmd.ts work on these tickets too. ──
defineAsyncCommand({
  name: "review-bg",
  description: "Run the 'review' crew in the background.",
  maxConcurrent: 2,
  args: { path: { type: "string", required: true } },
  dispatch: function (args, ctx) {
    const ticket = ctx.services.get("script-host").runCrewAsync("review", { path: args.path });
    ctx.log.info("launched (ticket " + ticket + ")");
    return { ticket: ticket };
  },
  completed: function (result, ctx) {
    ctx.writeLine("✓ review: " + result.payload);
  },
});
