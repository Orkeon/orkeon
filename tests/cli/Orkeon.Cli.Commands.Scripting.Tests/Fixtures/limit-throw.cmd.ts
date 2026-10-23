// R9: a completed() that hits an engine limit (unbounded recursion) must still release the slot.
defineAsyncCommand({
  name: "limit-throw",
  description: "Single-slot command whose completed handler overflows the recursion limit.",
  maxConcurrent: 1,
  dispatch: async (args, ctx) => {
    return { ticket: ctx.services.get("script-host").runCrewAsync("first") };
  },
  completed: (result, ctx) => {
    if (ctx.command.rawInput.indexOf("boom") >= 0) {
      const f = () => f() + 1;
      f();
    }
  },
});
