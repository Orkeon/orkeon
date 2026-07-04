// Phase 1 fixture: valid scripted command with no args schema and a sync handler.
// Fixtures are written as plain JS-in-TS-syntax so the PassThroughTranspiler can
// feed them straight to Jint without invoking esbuild (keeps tests fast and offline).

defineCommand({
  name: "hello",
  description: "Say hello to the world.",
  handler: function (args, ctx) {
    return ctx.continue("hello, world");
  },
});
