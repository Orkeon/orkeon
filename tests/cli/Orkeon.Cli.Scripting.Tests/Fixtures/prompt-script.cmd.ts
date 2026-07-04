// Phase 2 fixture: handler uses ctx.log + ctx.write + ctx.prompt(confirm).
// The test driver answers "y" → handler returns continue("ok").

defineCommand({
  name: "deploy",
  description: "Confirm a deployment.",
  handler: async function (args, ctx) {
    ctx.log.info("starting deploy");
    ctx.writeLine("about to deploy");
    const ok = await ctx.prompt({ type: "confirm", message: "Proceed?", default: false });
    if (!ok) return ctx.continue("aborted");
    return ctx.continue("ok");
  },
});
