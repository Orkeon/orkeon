namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>The crew documents the FWK-28 graph tests load: a triage decision that routes to a fix or an escalation.</summary>
internal static class YamlGraphTestCrews
{
    /// <summary>A graph crew of the ADR-018 example; <paramref name="graph"/> is the <c>graph:</c> block, <paramref name="extraTask"/> lines added under the <c>fix</c> task.</summary>
    internal static string Triage(string graph, string process = "graph", string extraTask = "") => $$"""
name: triage-crew
goal: Route a ticket
process: {{process}}
{{graph}}
agents:
  a: { role: R, goal: G }
tasks:
  triage:
    decision:
      questions:
        category:
          choice:
            bug: a defect
            other: anything else
  fix:
    description: Fix it
    expectedOutput: A fix
    agent: a
{{extraTask}}
  escalate:
    description: Escalate it
    expectedOutput: A page
    agent: a
""";

    /// <summary>The ADR-018 block: triage routes to fix on a bug, else to escalate.</summary>
    internal const string Branching = """
graph:
  start: triage
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage, to: fix, when: { triage.category: [bug] } }
    - { from: triage, to: escalate }
    - { from: fix,      to: END }
    - { from: escalate, to: END }
  maxTransitions: 50
  maxVisits: 5
  circuitBreakerPreset: strict
  maxTotalDurationSeconds: 600
""";
}
