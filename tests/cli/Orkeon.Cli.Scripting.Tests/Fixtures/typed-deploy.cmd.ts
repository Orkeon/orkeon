// Phase 3 fixture: typed args schema. Handler reads args.target/args.crew/args.dry.

defineCommand({
  name: "deploy",
  description: "Deploy a crew to a target.",
  args: {
    target: { type: "string", required: true, choices: ["dev", "preprod", "prod"] },
    crew:   { type: "string", required: true },
    dry:    { type: "boolean", default: false },
  },
  handler: function (args, ctx) {
    var mode = args.dry ? "DRY" : "APPLY";
    return ctx.continue("deploy " + args.crew + " -> " + args.target + " [" + mode + "]");
  },
});
