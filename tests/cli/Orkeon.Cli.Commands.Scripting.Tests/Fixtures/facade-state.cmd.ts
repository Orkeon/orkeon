// #493: the state option of the real script-host.runCrewAsync and of commands.post. The second word of the
// line picks the case: a state above the cap, a cycle or a function (refused), no state, a post, or a plain state.
defineAsyncCommand({
  name: "facade-state",
  description: "Launch with or without a state and print what completed receives.",
  maxConcurrent: 4,
  dispatch: (args, ctx) => {
    const host = ctx.services.get("script-host");
    const mode = ctx.command.rawInput.split(" ")[1];
    if (mode === "big" || mode === "cycle" || mode === "fn") {
      let state;
      if (mode === "big") state = { text: "x".repeat(16 * 1024) };
      else if (mode === "cycle") { state = {}; state.self = state; }
      else state = () => 1;
      try {
        host.runCrewAsync("one", undefined, { state: state });
        ctx.writeLine("launched");
      } catch (e) {
        ctx.writeLine("refused:" + (e && e.message ? e.message : e));
      }
      return { message: "done" };
    }
    if (mode === "none") return { ticket: host.runCrewAsync("one") };
    if (mode === "post") return { ticket: ctx.services.get("commands").post("echo", "run", "p", { state: { via: "post" } }) };
    const ticket = host.runCrewAsync("one", undefined, { state: { n: 1, list: ["x", 2, null] } });
    ctx.writeLine("launched:" + ticket);
    return { ticket: ticket };
  },
  completed: (result, ctx) => {
    ctx.writeLine("completed:" + result.ticket + ":" + (result.state === undefined ? "undefined" : JSON.stringify(result.state)));
  },
});
