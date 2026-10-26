// Async dispatch whose completed() blocks on a CLR service until the test releases it: the push drain must
// count as work in flight (CommandInstanceRegistry.HasRunning) until it ended.
defineAsyncCommand({
  name: "askbg-held",
  description: "Ask an agent in the background; completed() holds until released.",
  dispatch: (args, ctx) => {
    const ticket = ctx.services.get("commands").post("echo", "run", args.raw[0]);
    return { ticket: ticket };
  },
  completed: (result, ctx) => {
    ctx.services.get("held").Hold();
    ctx.writeLine("held-done:" + result.payload);
  },
});
