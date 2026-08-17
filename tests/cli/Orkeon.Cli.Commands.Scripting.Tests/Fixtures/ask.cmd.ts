// Sync dispatch (design §4.1): await the agent's response, then return.
defineCommand({
  name: "ask",
  description: "Ask an agent and wait for the reply.",
  async handler(args, ctx) {
    const res = await ctx.services.get("commands").request("echo", "run", args.raw[0]);
    return ctx.continue("reply:" + res.payload);
  },
});
