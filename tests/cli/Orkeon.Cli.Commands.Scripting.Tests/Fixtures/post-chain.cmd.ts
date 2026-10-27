// commands.post counts in the chain depth like runCrewAsync: each completed posts again, until the
// launch is refused at MaxChainDepth. Five relaunches without a refusal mean the depth was bypassed.
let stage = 0;
defineAsyncCommand({
  name: "post-chain",
  description: "Post to an agent; each completion posts again until the chain depth refuses it.",
  dispatch: (args, ctx) => {
    stage = 0;
    return { ticket: ctx.services.get("commands").post("echo", "run", "first") };
  },
  completed: (result, ctx) => {
    stage += 1;
    if (stage > 5) {
      ctx.writeLine("unbounded:" + stage);
      return;
    }
    try {
      const next = ctx.services.get("commands").post("echo", "run", "again-" + stage);
      ctx.writeLine("posted:" + next);
    } catch (e) {
      ctx.writeLine("refused:" + (e && e.message ? e.message : e));
    }
  },
});
