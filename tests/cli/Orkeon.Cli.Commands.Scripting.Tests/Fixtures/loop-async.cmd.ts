// #221: a driver loop of two hops (the command -> a crew -> the command's completed -> the crew again), six
// cycles long. Typed with "continue" in the line, each relaunch asks to keep the parent's chain depth.
let hops = 0;
defineAsyncCommand({
  name: "loop-async",
  description: "Relaunch a crew from completed six times, optionally at the parent's chain depth.",
  dispatch: (args, ctx) => {
    hops = 0;
    return { ticket: ctx.services.get("script-host").runCrewAsync("one") };
  },
  completed: (result, ctx) => {
    hops += 1;
    if (hops >= 6) {
      ctx.writeLine("loop-done:" + hops);
      return;
    }
    const options = ctx.command.rawInput.indexOf("continue") >= 0 ? { chain: "continue" } : undefined;
    try {
      const next = ctx.services.get("script-host").runCrewAsync("one", {}, options);
      ctx.writeLine("hop:" + hops + ":" + next);
    } catch (e) {
      ctx.writeLine("refused:" + (e && e.message ? e.message : e));
    }
  },
});
