> 🇫🇷 [Version française](../fr/adr/ADR-011-aspire-dashboard-observability.md)

> **See also**: [ADR-010](./ADR-010-agent-framework-interop.md) · [Orkeon Studio](../architecture/studio.md) · [Back to the index](../INDEX.md)

# ADR-011 — The Aspire dashboard is the cross-platform observability surface; there is no web Studio

**Status**: Accepted · **Date**: 2026-09-11
· **Scope**: `src/hosting/Orkeon.Hosting.Aspire`, `src/packaging/Orkeon.Hosting.Aspire`, `Orkeon.Hosting` (telemetry activation), `Orkeon.Studio.Wpf`

## Context

Two facts sat side by side. `Orkeon.Studio.Wpf` is the only project targeting
`net10.0-windows`: the desktop Studio — launch, watch, capture — exists for Windows alone,
and the question of a web Studio came up every time the platform list did. And the runners
carried a complete OpenTelemetry plumbing that exported nothing: `orkeon run` and
`orkeon-host` built their host with the parameterless `AddOrkeonInfrastructure()` (no
telemetry section), never started the host (so OpenTelemetry's hosted service never
created the providers), and ignored the standard `OTEL_EXPORTER_OTLP_ENDPOINT` — a
collector had to be named in Orkeon's own settings.

Meanwhile .NET Aspire ships, in every AppHost, a dashboard that renders traces, metrics
and structured logs of any process that speaks OTLP — cross-platform, maintained by
Microsoft, already open on the machine of the developer the README now targets.

## Decision

1. **The runners export OpenTelemetry by the standard contract.** `RunnerHost` registers
   telemetry from the settings *and* honours `OTEL_EXPORTER_OTLP_ENDPOINT` (with the
   protocol and headers the exporter reads itself) for traces, metrics and logs; it resolves
   the tracer and meter providers after building the host, since the runners never start
   it. Measured: with the environment variable alone, a quickstart run sends `v1/traces`
   (`invoke_agent Scribe`, `chat llama3.2:1b`, `execute_tool file_write` with their
   `gen_ai.*` attributes), `v1/metrics` and — at verbosity 1 — `v1/logs`.
2. **`Orkeon.Hosting.Aspire` is the AppHost integration**, a separate package on
   `Orkeon` + `Aspire.Hosting` (PUB-25 wrapper, ninth lineup id): `AddOrkeonHost` (the
   daemon) and `AddOrkeonCrewRun` (one run) as executable resources with the settings,
   mounts and `ORKEON_` environment an operator would pass by hand, `.WithOtlpExporter()`
   applied so the dashboard receives the run. Verified: an AppHost launched the quickstart
   crew as a resource, the crew wrote `out/hello.md`, Aspire handed the process the OTLP
   endpoint (pinned by a launch-free test on the evaluated environment).
3. **The Aspire dashboard is the observability surface for every platform. No web Studio
   will be built.** `Orkeon.Studio.Wpf` stays what it is — a Windows desktop application
   for launching, watching and capturing — and gains no web twin; what a web Studio would
   have shown (a run's spans, tokens, logs) the dashboard already shows, for anyone, with
   no code to maintain here. The observability half of the "Windows-only Studio" concern
   is closed by this decision; the launch half stays desktop.

## Consequences

- Any OTLP backend, not only Aspire, receives a run without Orkeon-specific settings:
  Langfuse, Honeycomb, Application Insights, an OpenTelemetry Collector.
- Telemetry is now *on* in the runners (an OpenTelemetry tracer provider listens to the
  `Orkeon.*` sources) even without an exporter; spans are created and dropped. The cost
  is a few allocations per LLM call; the benefit is that an exporter needs no restart to
  attach.
- The AppHost example (`examples/aspire/AppHost/`) needs `orkeon` on the PATH or
  `ORKEON_CLI`; the dashboard is Aspire's, its login token printed by the AppHost. A
  screenshot of the dashboard with a run belongs in `docs/assets/` once one is taken on a
  workstation — it was not taken in the session that made this decision.
