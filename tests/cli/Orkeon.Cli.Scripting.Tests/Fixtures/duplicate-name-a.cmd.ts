// Phase 1 fixture: collides with duplicate-name-b.cmd.ts on the name 'dup'.
// First in discovery order wins (this one, thanks to alphabetical sort).

defineCommand({
  name: "dup",
  description: "First definition — should win.",
  handler: function (args, ctx) {
    return ctx.continue("from-a");
  },
});
