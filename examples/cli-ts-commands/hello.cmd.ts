/// <reference path="./.orkeon/orkeon-cli.d.ts" />
//
// Minimal scripted command: `hello [--who <name>]`.
// Loaded automatically when you point the runner at this directory:
//
//   dotnet run --project src/apps/Orkeon.ConsoleApp -- \
//       --runner=scripted-commands --commands-dir examples/cli-ts-commands
//
// Then at the prompt:
//
//   scripted> help            # 'hello' shows up
//   scripted> hello           # → Hello, world!
//   scripted> hello --who=Cyril
//   scripted> help-cmd hello  # signature with typed args
//   scripted> exit

defineCommand({
  name: "hello",
  description: "Greet someone (or the world).",
  args: {
    who: { type: "string", default: "world" },
  },
  handler: function (args, ctx) {
    ctx.log.info("greeting from script", { who: args.who });
    return ctx.continue("Hello, " + args.who + "!");
  },
});
