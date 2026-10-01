// GAP-13 fixture: completed() replays with the line that launched ITS ticket, not the line
// of whichever later invocation happens to pump the drain queue.
defineAsyncCommand({
  name: "askbg-raw",
  description: "Ask an agent in the background, echo the launching line on completion.",
  dispatch(args, ctx) {
    const ticket = ctx.services.get("commands").post("echo", "run", args.raw[0]);
    return { ticket: ticket };
  },
  completed(result, ctx) {
    ctx.writeLine("done:" + result.payload + "@" + ctx.command.rawInput);
  },
});
