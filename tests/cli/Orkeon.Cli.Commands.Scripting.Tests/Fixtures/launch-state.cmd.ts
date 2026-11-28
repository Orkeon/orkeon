// #493: two instances in flight at once (maxConcurrent 2), each launch attaching its own state. Each completed
// prints the ticket and the state it received; the completed of `a` chains `c` with a state of its own.
defineAsyncCommand({
  name: "stateful",
  description: "Two instances at once, each launch attaching its own state.",
  maxConcurrent: 2,
  dispatch: (args, ctx) => {
    const crew = ctx.command.rawInput.split(" ")[1];
    const ticket = ctx.services.get("script-host").runCrewAsync(crew, { state: { turn: crew } });
    ctx.writeLine("launched:" + crew + ":" + ticket);
    return { ticket: ticket };
  },
  completed: (result, ctx) => {
    ctx.writeLine("completed:" + result.intent + ":" + result.ticket + ":" + JSON.stringify(result.state));
    if (result.state && result.state.turn === "a") {
      const next = ctx.services.get("script-host").runCrewAsync("c", { state: { turn: "c", from: result.ticket } });
      ctx.writeLine("launched:c:" + next);
    }
  },
});
