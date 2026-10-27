// Fixture: lists the session's usage by model and by command through ctx.services.get("usage") (#226).

defineCommand({
  name: "usage",
  description: "List the session's LLM usage by model and by command.",
  handler: async function (args, ctx) {
    if (!ctx.services.has("usage")) {
      ctx.writeLine("usage:unavailable");
      return ctx.continue();
    }
    const report = ctx.services.get("usage").report();
    const money = (cost) => (cost === null ? "n/a" : "$" + cost.toFixed(4));
    ctx.writeLine("total " + report.totalTokens + " tokens, " + report.calls + " calls, " + money(report.cost));
    report.byModel
      .map((m) => "model " + m.model + " in=" + m.promptTokens + " out=" + m.completionTokens
        + " cache=" + m.cacheHitTokens + " reasoning=" + m.reasoningTokens + " " + money(m.cost))
      .forEach((line) => ctx.writeLine(line));
    report.byCommand
      .map((c) => "command " + c.ticket + " " + c.name + " in=" + c.promptTokens + " out=" + c.completionTokens
        + " cache=" + c.cacheHitTokens + " calls=" + c.calls + " " + money(c.cost))
      .forEach((line) => ctx.writeLine(line));
    return ctx.continue();
  },
});
