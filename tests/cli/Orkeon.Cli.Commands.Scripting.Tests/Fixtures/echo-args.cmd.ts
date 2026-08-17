// Phase 1 fixture for the integration test: echoes raw args.
// Confirms that Phase 1 hands args as { raw: string[] } per spec §4.3.

defineCommand({
  name: "echo",
  description: "Echo the raw args back.",
  handler: function (args, ctx) {
    return ctx.continue("echo:" + args.raw.join(","));
  },
});
