// Async dispatch whose body suspends on a genuinely pending CLR task BEFORE launching
// (the /assistant shape since B-5: read session state, then post). Pins the fix for the
// deferred-dispatch race: the launch must complete before the prompt returns, not at the
// next engine pump.
defineAsyncCommand({
  name: "askbg-async",
  description: "Ask an agent in the background after an async preamble.",
  maxConcurrent: 1,
  dispatch: async (args, ctx) => {
    await ctx.services.get("slow").WaitAsync();
    const ticket = ctx.services.get("commands").post("echo", "run", args.raw[0]);
    ctx.log.info("launched " + ticket);
    return { ticket: ticket };
  },
  completed: (result, ctx) => {
    ctx.writeLine("done:" + result.payload);
  },
});
