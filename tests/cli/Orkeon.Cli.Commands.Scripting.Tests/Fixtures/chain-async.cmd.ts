// Chained async dispatch (RT-20): the first launch's `completed` handler launches a second
// background run (runCrewAsync, the real facade) whose own `completed` must drain to this command.
let stage = 0;
defineAsyncCommand({
  name: "chain-async",
  description: "Launch a background run whose completion launches a second one.",
  maxConcurrent: 4,
  dispatch: async (args, ctx) => {
    const ticket = ctx.services.get("script-host").runCrewAsync("one");
    return { ticket: ticket };
  },
  completed: (result, ctx) => {
    stage += 1;
    if (stage === 1) {
      try {
        const second = ctx.services.get("script-host").runCrewAsync("two");
        ctx.writeLine("chained:" + second);
      } catch (e) {
        ctx.writeLine("refused:" + (e && e.message ? e.message : e));
      }
    } else {
      ctx.writeLine("second-done:" + result.payload);
    }
  },
});
