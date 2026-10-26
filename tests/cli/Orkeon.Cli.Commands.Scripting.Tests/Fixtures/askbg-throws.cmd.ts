// Async dispatch whose completed() throws: the drain reports the failure to the registry
// (CommandInstanceRegistry.CompletionFailed), so a replay stops on the line that launched it.
defineAsyncCommand({
  name: "askbg-throws",
  description: "Ask an agent in the background; completed() throws.",
  dispatch: (args, ctx) => {
    const ticket = ctx.services.get("commands").post("echo", "run", args.raw[0]);
    return { ticket: ticket };
  },
  completed: (result, ctx) => {
    throw new Error("render broke");
  },
});
