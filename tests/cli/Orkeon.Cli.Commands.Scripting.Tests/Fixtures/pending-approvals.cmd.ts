// Fixture: lists the tool approvals waiting for the operator through ctx.services.get("approvals").

defineCommand({
  name: "pending",
  description: "List the tool approvals waiting for the operator.",
  handler: async function (args, ctx) {
    if (!ctx.services.has("approvals")) {
      ctx.writeLine("approvals:unavailable");
      return ctx.continue();
    }
    const pending = ctx.services.get("approvals").list();
    ctx.writeLine("approvals:" + pending.length);
    pending
      .map((a) => "[" + a.taskId + "] " + a.command + " waits on " + a.tool + " " + a.input)
      .forEach((line) => ctx.writeLine(line));
    ctx.writeLine("json:" + JSON.stringify(pending));
    return ctx.continue();
  },
});
