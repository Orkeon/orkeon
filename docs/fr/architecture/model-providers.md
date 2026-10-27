> 🇬🇧 [English version](../../architecture/model-providers.md)

> **Voir aussi** : [Fournisseurs LLM](./llm-providers.md) · [Modèles de décision](./decision-models.md) · [ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md) · [Retour à l'index](../INDEX.md)

# Fournisseurs de modèles

Orkeon parle à des fournisseurs de modèles distants rangés en quatre **catégories**
(`ModelProviderCategory`, `Orkeon.Constants.Llm`). Une catégorie dit à quoi sert un fournisseur ;
chacune garde son propre contrat, et aucune interface n'est commune à toutes
([ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md)).

| Catégorie | Contrat | Ce qu'il renvoie | Guide |
|-----------|---------|------------------|-------|
| `Generation` | `ILlmProvider` (`Orkeon.Domain`) | du texte, des appels de tools, un flux | [Fournisseurs LLM](./llm-providers.md) |
| `Embedding` | `IEmbeddingProvider` (`Orkeon.Application`) | des vecteurs | [Système de mémoire](./memory-system.md), [Pipeline RAG](./rag-pipeline.md) |
| `Reranking` | `IReranker` (`Orkeon.Rag.Abstractions`) | des passages dans un nouvel ordre | [Pipeline RAG](./rag-pipeline.md) |
| `Decision` | `IDecisionModel` (`Orkeon.Application`) | des décisions typées avec des probabilités calibrées | [Modèles de décision](./decision-models.md) |

## Le catalogue

`ModelProviderCatalog.All` liste chaque fournisseur distant une fois, avec ses catégories, la
variable d'où se lit sa clé d'API et son endpoint par défaut :

- les 16 fournisseurs de génération, dans l'ordre de `LlmProviderKeys.All` ; OpenAI et Ollama
  servent aussi `Embedding` ;
- TypeSafe, qui ne sert que `Decision` (`TYPESAFE_API_KEY`, `https://api.typesafe.ai/v1`).

`ModelProviderCatalog.InCategory(category)` le filtre, et `Find(key)` résout une clé ou l'un de
ses alias. Un test vérifie que chaque fournisseur que construit `LlmProviderFactory` figure au
catalogue sous `Generation` ; `scripts/check-doc-claims.py` lit le nombre de fournisseurs dans le
code.

Le catalogue ne liste que des services distants. Un reranker ou un embedder qui tourne dans le
processus (ONNX, le modèle d'embedding local), et un composant bâti sur une autre catégorie (le
reranker listwise par LLM, le reranker de décision) ne sont pas des fournisseurs à part entière.

## Ajouter un fournisseur

- Un fournisseur de **génération** suit [Fournisseurs LLM](./llm-providers.md) et ajoute sa ligne
  au catalogue.
- Un fournisseur d'**une autre catégorie** implémente le contrat de cette catégorie et ajoute sa
  ligne avec ses catégories. Un second fournisseur de décision implémente `IDecisionModel` ; les
  étapes, la garde, le reranker et le tool l'utilisent sans changement.
- Une **nouvelle catégorie** ajoute une valeur à l'enum et son propre port ; elle n'élargit
  jamais `ILlmProvider`.
