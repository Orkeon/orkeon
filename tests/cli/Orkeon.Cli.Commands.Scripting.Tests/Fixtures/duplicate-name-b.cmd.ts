// Phase 1 fixture: collides with duplicate-name-a.cmd.ts.
// The loader logs Warning and ignores this descriptor.

defineCommand({
  name: "dup",
  description: "Second definition — should lose.",
  handler: function (args, ctx) {
    return ctx.continue("from-b");
  },
});
