// GAP-13 fixture: echoes the line the user typed, as the handler sees it.
defineCommand({
  name: "deploy",
  description: "Echo ctx.command.rawInput.",
  handler: function (args, ctx) {
    return ctx.continue("raw:" + ctx.command.rawInput + "|name:" + ctx.command.name);
  },
});
