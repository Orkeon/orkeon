// Fixture: prints the version and the install channel of the runtime through ctx.services.get("runtime").

defineCommand({
  name: "runtime-version",
  description: "Print the version of the runtime that runs this command.",
  handler: async function (args, ctx) {
    const runtime = ctx.services.get("runtime");
    const full = runtime.commit ? runtime.version + "+" + runtime.commit : runtime.version;
    ctx.writeLine("version:" + full);
    ctx.writeLine("channel:" + runtime.channel);
    return ctx.continue();
  },
});
