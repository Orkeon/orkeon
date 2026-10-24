// R9: two commands of one file share one completion drain queue. pair-a can park its dispatch
// (holding the shared engine lock) so the test decides which command drains pair-b's completion.
defineAsyncCommand({
  name: "pair-a",
  description: "Second command of the file; drains the shared queue.",
  maxConcurrent: 4,
  dispatch: async (args, ctx) => {
    const host = ctx.services.get("script-host");
    if (ctx.command.rawInput.indexOf("park") >= 0) {
      host.park();
    }
    return { ticket: host.runCrewAsync("a-run") };
  },
});

defineAsyncCommand({
  name: "pair-b",
  description: "Single-slot command with a completed handler.",
  maxConcurrent: 1,
  dispatch: async (args, ctx) => {
    return { ticket: ctx.services.get("script-host").runCrewAsync("b-run") };
  },
  completed: (result, ctx) => {
    ctx.writeLine("b-completed");
  },
});
