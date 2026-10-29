// #239: a command whose crew must not run twice. The dispatch launches it exclusively; its completed relaunches it
// exclusively once — the holder is terminal by then, so the relaunch is admitted.
let relaunched = false;
defineAsyncCommand({
  name: "exclusive-async",
  description: "Launch a crew exclusively, then relaunch it exclusively from completed.",
  dispatch: (args, ctx) => {
    relaunched = false;
    return { ticket: ctx.services.get("script-host").runCrewAsync("one", {}, { exclusive: true }) };
  },
  completed: (result, ctx) => {
    if (relaunched) {
      ctx.writeLine("exclusive-done:" + result.payload);
      return;
    }
    relaunched = true;
    try {
      ctx.writeLine("relaunched:" + ctx.services.get("script-host").runCrewAsync("one", {}, { exclusive: true }));
    } catch (e) {
      ctx.writeLine("refused:" + (e && e.message ? e.message : e));
    }
  },
});
