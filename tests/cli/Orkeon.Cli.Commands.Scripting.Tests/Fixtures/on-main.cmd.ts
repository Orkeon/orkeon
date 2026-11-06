// Work launched on the main thread: each launch is named by its rank, and `completed` says what it received.
let launches = 0;
defineAsyncCommand({
  name: "on-main",
  description: "Launch gated work that writes on the main thread.",
  maxConcurrent: 4,
  dispatch: async (args, ctx) => {
    launches += 1;
    const ticket = ctx.services.get("script-host").runOnMain(launches === 1 ? "first" : "second");
    return { ticket: ticket };
  },
  completed: (result, ctx) => {
    ctx.writeLine("completed:" + result.success + ":" + result.payload);
  },
});
