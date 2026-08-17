// Async dispatch (design §4.2): post and return the prompt; completed() replays later.
defineAsyncCommand({
  name: "askbg",
  description: "Ask an agent in the background.",
  maxConcurrent: 1,
  dispatch(args, ctx) {
    const ticket = ctx.services.get("commands").post("echo", "run", args.raw[0]);
    ctx.log.info("launched " + ticket);
    return { ticket: ticket };
  },
  completed(result, ctx) {
    ctx.writeLine("done:" + result.payload);
  },
});
