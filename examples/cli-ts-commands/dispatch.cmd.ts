/// <reference path="./.orkeon/orkeon-cli.d.ts" />
//
// Command dispatch to live agents (design §8). These commands address an agent by name
// through `ctx.services.get("commands")`; the agent must have declared `onCommand` (see the
// companion .ork.ts crew) and been activated on the dispatch bus.
//
//   scripted> ask hello          # sync: blocks, prints the agent reply
//   scripted> ask-bg hello       # async: returns a ticket immediately
//   scripted> ps                 # list in-flight commands
//   scripted> inspect --ticket=t1
//   scripted> result --ticket=t1
//   scripted> cancel --ticket=t1

// ── Sync (design §4.1): await the agent's response, then return. ──
defineCommand({
  name: "ask",
  description: "Ask the 'echo' agent and wait for the reply.",
  args: { text: { type: "string", required: true } },
  handler: function (args, ctx) {
    const res = ctx.services.get("commands").request("echo", "run", args.text);
    return res.success
      ? ctx.continue("→ " + res.payload)
      : ctx.continue("agent error: " + res.error);
  },
});

// ── Async (design §4.2): post and detach; completed() replays at the next pump. ──
defineAsyncCommand({
  name: "ask-bg",
  description: "Ask the 'echo' agent in the background.",
  maxConcurrent: 3, // at most 3 in-flight instances of THIS command
  args: { text: { type: "string", required: true } },
  dispatch: function (args, ctx) {
    const ticket = ctx.services.get("commands").post("echo", "run", args.text);
    ctx.log.info("launched (ticket " + ticket + ")");
    return { ticket: ticket };
  },
  completed: function (result, ctx) {
    ctx.writeLine("✓ " + result.agent + ": " + result.payload);
  },
});
