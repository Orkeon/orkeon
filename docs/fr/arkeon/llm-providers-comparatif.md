> 🇬🇧 [English version](../../arkeon/llm-providers-comparatif.md)

# Comparatif des fournisseurs LLM — Orkeon

> État au 2026-06-12, dérivé du code source (`src/core/Orkeon.Infrastructure/LLMs/`).
> Légende : ✓ supporté · ✗ absent · ◐ partiel/générique.

| Fournisseur | Classe de base | Streaming SSE | Tool calling natif | Chat multi-tours (rôles tool) | Message système | top_p / stop | Grammaire GBNF | response_format JSON | thinking / reasoning_effort | reasoning_content round-trip | Métriques cache prompt | Métriques timing | Résilience Polly | Sanitization clé API |
|---|---|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| **DeepSeek** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✓ | ✓ |
| **OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Groq** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✓ | ✓ | ✓ |
| **Together AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Qwen** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Kimi / Moonshot** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Mistral AI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **HuggingFace** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |
| **Anthropic** | HttpLlmProviderBase | ✓ | ✓ | ✓ | ✓ (séparé natif) | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ |
| **Ollama** | HttpLlmProviderBase | ✓ | ✗ | ◐ (concat) | ✓ (prepend) | ✗ | ✓ | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ | ✓ |
| **Azure OpenAI** | OpenAI-compat | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✗ | ✗ | ✗ | ◐ | ✗ | ✓ | ✓ |

## Notes

- **◐ cache prompt** (OpenAI-compat) : `OpenAICompatibleProviderBase.ParseSuccessResponse` parse génériquement `prompt_cache_hit_tokens` / `prompt_cache_miss_tokens`, mais seul DeepSeek les émet réellement côté API.
- **◐ chat multi-tours** (Ollama) : pas d'override `ChatAsync` → repli sur la concaténation de prompts de `HttpLlmProviderBase`, sans rôles `tool` / `tool_call_id`.
- **Azure OpenAI** (R10.7) : désormais bâti sur `OpenAICompatibleProviderBase` avec la stratégie de tool calling natif OpenAI câblée par `LlmProviderFactory` ; l'endpoint par déploiement (`/openai/deployments/{model}/chat/completions?api-version=…`) et l'authentification `api-key` sont préservés.
- **top_p / stop** : Ollama n'expose que `temperature` + `num_predict` (= max_tokens).
- **Grammaire GBNF** : injectée dans le chemin `BuildRequestPayload` (prompt), pour les backends compatibles llama.cpp/vLLM et Ollama.
- **Polly** et **Sanitization clé API** : fournis par `HttpLlmProviderBase` → actifs sur tous les fournisseurs.

## Particularités DeepSeek (le plus complet)

Seul fournisseur à câbler :

- le round-trip `reasoning_content` (re-émis verbatim à chaque tour, obligatoire en thinking mode sinon HTTP 400) ;
- le bloc `thinking` (`enabled`/`disabled`) + `reasoning_effort` ;
- `response_format: json_object` avec garde-fou loggé si le prompt ne contient pas « json » ;
- les métriques de context caching (`prompt_cache_hit/miss_tokens`, facturées 1/10 du prix d'entrée).

Couverture de tests : `DeepSeekCacheMetricsTests`, `DeepSeekReasoningRoundTripTests`, `DeepSeekResponseFormatPayloadTests`, `DeepSeekResponseFormatStreamingTests`, `DeepSeekThinkingPayloadTests`.
