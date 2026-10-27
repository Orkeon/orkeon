// #220: an async `completed` reads the session store (a Task-backed host read, "session"), then launches the
// next crew. The drain awaits the returned promise, so the launch made after the await is chained to this
// command and its own completion drains here.
defineAsyncCommand({
  name: "async-completed",
  description: "Run a crew; its completion reads the session store, then launches the next crew.",
  dispatch: (args, ctx) => ({ ticket: ctx.services.get("script-host").runCrewAsync("one") }),
  completed: async (result, ctx) => {
    if (result.payload === "one-out") {
      const mode = await ctx.services.get("session").readMode();
      ctx.writeLine("mode:" + mode);
      const next = ctx.services.get("script-host").runCrewAsync("two", { mode: mode });
      ctx.writeLine("launched:" + next);
    } else {
      ctx.writeLine("second-done:" + result.payload);
    }
  },
});
