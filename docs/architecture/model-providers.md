> 🇫🇷 [Version française](../fr/architecture/model-providers.md)

> **See also**: [LLM providers](./llm-providers.md) · [Decision models](./decision-models.md) · [ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md) · [Back to the index](../INDEX.md)

# Model providers

Orkeon speaks to remote model providers in four **categories** (`ModelProviderCategory`,
`Orkeon.Constants.Llm`). A category says what a provider is used for; each keeps its own
contract, and there is no interface common to all of them
([ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md)).

| Category | Contract | What it returns | Guide |
|----------|----------|-----------------|-------|
| `Generation` | `ILlmProvider` (`Orkeon.Domain`) | text, tool calls, a stream | [LLM providers](./llm-providers.md) |
| `Embedding` | `IEmbeddingProvider` (`Orkeon.Application`) | vectors | [Memory system](./memory-system.md), [RAG pipeline](./rag-pipeline.md) |
| `Reranking` | `IReranker` (`Orkeon.Rag.Abstractions`) | passages in a new order | [RAG pipeline](./rag-pipeline.md) |
| `Decision` | `IDecisionModel` (`Orkeon.Application`) | typed decisions with calibrated probabilities | [Decision models](./decision-models.md) |

## The catalogue

`ModelProviderCatalog.All` lists every remote provider once, with its categories, the variable
its API key is read from and its default endpoint:

- the 16 generation providers, in `LlmProviderKeys.All` order; OpenAI and Ollama also serve
  `Embedding`;
- TypeSafe, which serves `Decision` only (`TYPESAFE_API_KEY`, `https://api.typesafe.ai/v1`).

`ModelProviderCatalog.InCategory(category)` filters it, and `Find(key)` resolves a key or one of
its aliases. A test checks that every provider `LlmProviderFactory` builds is in the catalogue
under `Generation`; `scripts/check-doc-claims.py` reads the provider count from the code.

The catalogue lists remote services only. A reranker or embedder running in process (ONNX, the
local embedding model), and one built over another category (the listwise LLM reranker, the
decision reranker) are not providers of their own.

## Adding a provider

- A **generation** provider follows [LLM providers](./llm-providers.md) and adds its catalogue
  line.
- A provider of **another category** implements that category's contract and adds its line
  with its categories. A second decision provider implements `IDecisionModel`; the stages, the
  guard, the reranker and the tool use it unchanged.
- A **new category** adds an enum value and its own port; it never widens `ILlmProvider`.
