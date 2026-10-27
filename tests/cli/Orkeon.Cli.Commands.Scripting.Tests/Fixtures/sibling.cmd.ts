// Two commands of one file share one engine and one completion drain queue. Each must receive only the
// completions of the runs it started: sibling-b can park its dispatch (holding the shared engine lock) so
// the test decides which command pumps the queue first.
defineAsyncCommand({
  name: "sibling-a",
  description: "Launch a run; its completed reports the command it runs as.",
  maxConcurrent: 4,
  dispatch: (args, ctx) => ({ ticket: ctx.services.get("script-host").runCrewAsync("a-run") }),
  completed: (result, ctx) => {
    ctx.writeLine("a-completed:" + result.payload + " as " + ctx.command.name);
  },
});

defineAsyncCommand({
  name: "sibling-b",
  description: "Launch a run, optionally parking first; its completed reports the command it runs as.",
  maxConcurrent: 4,
  dispatch: (args, ctx) => {
    const host = ctx.services.get("script-host");
    if (ctx.command.rawInput.indexOf("park") >= 0) {
      host.park();
    }
    return { ticket: host.runCrewAsync("b-run") };
  },
  completed: (result, ctx) => {
    ctx.writeLine("b-completed:" + result.payload + " as " + ctx.command.name);
  },
});
