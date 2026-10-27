> 🇫🇷 [Version française](../fr/adr/ADR-015-model-provider-categories-and-decision-stages.md)

> **See also**: [Model providers](../architecture/model-providers.md) · [Decision models](../architecture/decision-models.md) · [LLM providers](../architecture/llm-providers.md) · [Back to the index](../INDEX.md)

# ADR-015 — Model provider categories, and decision stages that judge without rewriting

**Status**: Accepted · **Date**: 2026-10-27
· **Scope**: `Orkeon.Constants.Llm` (`ModelProviderCategory`, `ModelProviderCatalog`), `Orkeon.Application` (`IDecisionModel`, `IDecisionStage`, `DecisionVerdict`, the decision DTOs), `Orkeon.Infrastructure` (`Decision/TypeSafe/`, `LLMs/Decision/`, `system_one_decide`), `Orkeon.Rag` (`DecisionModelReranker`)

## Context

TypeSafe serves **Jev**, a "System One" model. It takes a state (text, JSON or images) and typed
questions, and answers with decisions: a `choice` among options, a `score` on an ordered scale, a
`noul` (the probability of a yes) or a `box` in an image, each with calibrated probabilities and a
confidence. It writes no text. Its own documentation presents it as a complement to an LLM, not
a replacement.

Orkeon knew one kind of model provider: the 16 generative providers behind `ILlmProvider`.
Embedders and rerankers already existed, each with its own contract, but nothing named them as
categories. Jev fits none of these contracts: forcing it behind `ILlmProvider` would give it a
`messages → text` signature it cannot honour, and every capability check would have to exclude it.

## Decision

1. **Model providers come in categories**: `Generation`, `Embedding`, `Reranking` and `Decision`
   (`ModelProviderCategory`). A category is a description, not a contract: each keeps its own
   interface, and there is no common base. `ModelProviderCatalog` lists every remote provider
   with its categories, its key variable and its default endpoint; the documentation counts and
   the doc-claims gate read it. A provider serving several categories (OpenAI, Ollama) appears
   once.
2. **`Decision` has its own port**, `IDecisionModel` (`DecideAsync`, `ListModelsAsync`), with
   typed DTOs: polymorphic questions (`choice`, `score`, `noul`, `box`) and answers.
   `TypeSafeDecisionModel` is its first adapter, opt-in through `AddOrkeonTypeSafe`. The count of
   LLM providers stays at 16.
3. **The main use is a decision stage around an LLM call.** `DecisionLlmProvider` is an
   `ILlmProvider` decorator that runs stages before and after the call it wraps. A stage
   (`IDecisionStage`) returns a `DecisionVerdict`: `Continue`, `Block`, `Route` or `Escalate`.
   **A stage judges and never rewrites**: the messages and the response pass through unchanged,
   and the code acts on the verdict. Three stages ship (`route`, `guard`, `verify`); a host adds
   its own in C#.
4. **A decision profile is an LLM profile.** It is declared under `Orkeon:Decision:Profiles`,
   registered through `AddOrkeonLlmProfile`, and a crew uses it with `llm: { profile: <name> }`.
   It may name LLM profiles only, never another decision profile, so no cycle can form; the host
   refuses an unknown or circular target when it builds.
5. **When the decision model fails, the call goes on** with a warning (fail-open), unless the
   profile sets `FailClosed`, in which case it is blocked.
6. **The other uses are secondary** and reuse the same port: the agent input guard
   `TypeSafeInputGuard`, the RAG reranker `decision` (alias `typesafe`) and the tool
   `system_one_decide`.

## Consequences

- A new kind of model (speech, vision, a second decision vendor) adds a category and a port;
  it does not bend `ILlmProvider`.
- A decision stage costs one extra round trip per phase it runs in. A `Route` or an `Escalate`
  after the response re-calls the LLM once; a second one is logged and ignored, so a profile
  never loops.
- The stage sees only what it is given: the request stages ask about the last user message, the
  response stages about the request and the response. A decision on the whole conversation needs
  a custom stage.
- The meter counts a decision call as `decision` usage, once, at the adapter.
- The `GuardPhase` of the agent pipeline has no `Output` phase; output filtering goes through a
  response stage of a decision profile ([Known limitations](../reference/limitations.md)).

## Alternatives considered

- **A 17th LLM provider**: rejected. `ILlmProvider` promises text, and every consumer (agents,
  the planner, the consensus) would have had to know that this one does not produce any.
- **A stage that rewrites the prompt or the response**: rejected. A rewriting filter hides what
  the model said and makes a failure hard to audit; a verdict is logged, audited and testable.
- **A common `IModelProvider` interface across categories**: rejected. The contracts share
  nothing but a name; a common interface would only carry casts.
