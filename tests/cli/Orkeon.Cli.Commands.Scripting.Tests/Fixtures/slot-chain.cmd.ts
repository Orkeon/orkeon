// R9: the admission slot (maxConcurrent 1) must stay held while a chain launched from a
// `completed` handler runs. Launches are parked by the test through the "script-host" double.
defineAsyncCommand({
  name: "slot-chain",
  description: "Launch one or two background runs; the first completion launches a chained one.",
  maxConcurrent: 1,
  dispatch: async (args, ctx) => {
    const host = ctx.services.get("script-host");
    const first = host.runCrewAsync("first");
    if (ctx.command.rawInput.indexOf("two") >= 0) {
      host.runCrewAsync("second");
    }
    return { ticket: first };
  },
  completed: (result, ctx) => {
    if (result.payload === "first-out") {
      const t = ctx.services.get("script-host").runCrewAsync("chain");
      ctx.writeLine("chained:" + t);
    } else if (result.payload === "chain-out") {
      ctx.writeLine("chain-done");
    }
  },
});
