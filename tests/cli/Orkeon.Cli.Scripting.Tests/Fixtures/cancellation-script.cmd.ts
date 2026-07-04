// Phase 2 fixture: handler observes ctx.signal.IsCancellationRequested in a busy-wait.
// Used by the cancellation test to verify Ctrl+C / RequestCommandCancellation propagates.

defineCommand({
  name: "wait",
  description: "Wait until cancelled.",
  handler: async function (args, ctx) {
    while (!ctx.signal.IsCancellationRequested) {
      // Yield to let the host signal cancellation; Jint awaits CLR Tasks correctly.
      await new Promise(function (resolve) { resolve(undefined); });
    }
    return ctx.continue("cancelled-from-script");
  },
});
