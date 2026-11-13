> 🇬🇧 [English version](../../reference/llm-campaign-results.md)

# Résultats des campagnes LLM

> Page générée par `llmproviders-test/lib/recap.sh` (ou `recap.ps1`) à la fin de chaque
> campagne : ne pas la modifier à la main.

Chaque modèle que le kit de campagne a exercé contre l'API réelle de son fournisseur, avec le
dernier résultat de chaque mode. Une ligne réunit les rapports du dernier jour de campagne du
modèle : seuls les rapports d'un même modèle sont réunis ; un compagnon vision est une ligne à part.
Les jours plus anciens sont dans le [journal des campagnes](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/README.md#campaigns-run).

Les modes M1 à M18 sont ceux du chat, D1 à D8 ceux des modèles de décision ; ils sont définis dans
le [README du kit](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/README.md#the-protocol-modes). ✅ réussi, ❌ en échec, ➖ sans
objet (le fournisseur ou le modèle ne déclare pas la capacité). Les modèles par défaut et les
raisons de leur choix : [comparatif des fournisseurs LLM](llm-providers-comparison.md).

Dernière campagne : 2026-11-13 · 74 modèles · 453 rapports archivés.

| Fournisseur | Modèle | Date | ✅/❌/➖ | Modes en échec | Rapport |
|---|---|---|---|---|---|
| Anthropic | `claude-sonnet-5` *(défaut)* | 2026-11-13 | 4/0/1 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/anthropic/2026-11-13-191424-claude-sonnet-5.md) |
|  | `claude-fable-5-1` | 2026-11-13 | 3/1/1 | M15 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/anthropic/2026-11-13-191508-claude-fable-5-1.md) |
|  | `claude-fable-5-2` | 2026-11-13 | 3/1/1 | M15 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/anthropic/2026-11-13-191650-claude-fable-5-2.md) |
|  | `claude-haiku-5-5` | 2026-11-13 | 4/0/1 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/anthropic/2026-11-13-191600-claude-haiku-5-5.md) |
|  | `claude-opus-5-5` | 2026-11-13 | 3/1/1 | M15 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/anthropic/2026-11-13-191622-claude-opus-5-5.md) |
|  | `claude-sonnet-5-5` | 2026-11-13 | 3/1/1 | M15 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/anthropic/2026-11-13-191531-claude-sonnet-5-5.md) |
|  | `claude-opus-5` | 2026-10-07 | 12/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/anthropic/2026-10-07-181520-claude-opus-5.md) |
| DeepSeek | `deepseek-flash` *(défaut)* | 2026-11-13 | 4/0/1 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/deepseek/2026-11-13-191423-deepseek-flash.md) |
|  | `deepseek-v4-pro` | 2026-10-07 | 11/1/0 | M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/deepseek/2026-10-07-181412-deepseek-v4-pro.md) |
|  | `deepseek-v4-flash` | 2026-09-07 | 10/2/0 | M2, M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/deepseek/2026-09-07-061452-deepseek-v4-flash.md) |
|  | `deepseek-v4-flash-vision-exp` | 2026-09-07 | 0/1/0 | M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/deepseek/2026-09-07-061540-deepseek-v4-flash-vision-exp.md) |
| Google Gemini | `gemini-3.8-flash` *(défaut)* | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/gemini/2026-11-13-191426-gemini-3.8-flash.md) |
|  | `gemini-3.7-flash` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/gemini/2026-11-13-191518-gemini-3.7-flash.md) |
|  | `gemini-4-argon` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/gemini/2026-11-13-191609-gemini-4-argon.md) |
|  | `gemini-4-carbon` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/gemini/2026-11-13-191803-gemini-4-carbon.md) |
|  | `gemini-3.1-pro-preview` | 2026-10-07 | 10/1/1 | M5 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/gemini/2026-10-07-181413-gemini-3.1-pro-preview.md) |
|  | `gemini-3.5-flash-lite` | 2026-10-07 | 12/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/gemini/2026-10-07-181522-gemini-3.5-flash-lite.md) |
| Grok (x.AI) | `grok-4.6` *(défaut)* | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/grok/2026-11-13-191425-grok-4.6.md) |
|  | `grok-4.7` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/grok/2026-11-13-191538-grok-4.7.md) |
| HuggingFace | `meta-llama/Llama-3.1-8B-Instruct` *(défaut)* | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/huggingface/2026-11-13-191423-meta-llama_Llama-3.1-8B-Instruct.md) |
|  | `Qwen/Qwen3.5-9B` | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/huggingface/2026-11-13-191427-Qwen_Qwen3.5-9B.md) |
|  | `Qwen/Qwen3-VL-30B-A3B-Instruct` | 2026-11-11 | 1/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/huggingface/2026-11-11-074404-Qwen_Qwen3-VL-30B-A3B-Instruct.md) |
| Kimi (Moonshot AI) | `kimi-k3.1-pro` *(défaut)* | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/kimi/2026-11-13-191426-kimi-k3.1-pro.md) |
|  | `kimi-k2.6` | 2026-11-13 | 4/0/1 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/kimi/2026-11-13-191528-kimi-k2.6.md) |
|  | `kimi-k2.7-code` | 2026-11-13 | 3/1/1 | M15 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/kimi/2026-11-13-191920-kimi-k2.7-code.md) |
|  | `kimi-k3` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/kimi/2026-11-13-191822-kimi-k3.md) |
|  | `kimi-k2.7-code-highspeed` | 2026-11-11 | 12/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/kimi/2026-11-11-064847-kimi-k2.7-code-highspeed.md) |
|  | `kimi-k3.1-pro-highspeed` | 2026-11-11 | 12/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/kimi/2026-11-11-064847-kimi-k3.1-pro-highspeed.md) |
| Mammouth AI | `gemini-3.8-flash` *(défaut)* | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mammouth/2026-11-13-191432-gemini-3.8-flash.md) |
|  | `gemini-3.7-flash` | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mammouth/2026-11-13-191504-gemini-3.7-flash.md) |
|  | `kimi-k3.1-pro` | 2026-11-11 | 10/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mammouth/2026-11-11-062558-kimi-k3.1-pro.md) |
|  | `deepseek-v4.1-flash` | 2026-10-07 | 1/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mammouth/2026-10-07-181416-deepseek-v4.1-flash.md) |
|  | `mammouth-recommended` | 2026-10-07 | 1/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mammouth/2026-10-07-181412-mammouth-recommended.md) |
| MiniMax | `MiniMax-M3` *(défaut)* | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/minimax/2026-11-13-191423-MiniMax-M3.md) |
|  | `MiniMax-M2` | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/minimax/2026-11-13-191433-MiniMax-M2.md) |
|  | `MiniMax-M3.1-Flash` | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/minimax/2026-11-13-191617-MiniMax-M3.1-Flash.md) |
|  | `MiniMax-M2.7` | 2026-10-07 | 8/2/2 | M2, M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/minimax/2026-10-07-181412-MiniMax-M2.7.md) |
| Mistral AI | `mistral-medium-2604` *(défaut)* | 2026-11-13 | 2/1/2 | M16 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mistral/2026-11-13-191423-mistral-medium-2604.md) |
|  | `mistral-large-4` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mistral/2026-11-13-191507-mistral-large-4.md) |
|  | `mistral-small-2603` | 2026-11-13 | 2/1/2 | M16 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mistral/2026-11-13-191557-mistral-small-2603.md) |
|  | `deepseek-v4-1-flash-1017` | 2026-11-11 | 11/1/0 | M2 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mistral/2026-11-11-062525-deepseek-v4-1-flash-1017.md) |
|  | `zai-glm-5-3-flash` | 2026-11-11 | 11/1/0 | M2 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/mistral/2026-11-11-062534-zai-glm-5-3-flash.md) |
| Ollama | `llama3.2` *(défaut)* | 2026-09-07 | 8/3/1 | M2, M7, M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/ollama/2026-09-07-054010-llama3.2.md) |
|  | `llava` | 2026-09-07 | 1/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/ollama/2026-09-07-054032-llava.md) |
| OpenAI | `gpt-5.6-sol` *(défaut)* | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-11-13-191424-gpt-5.6-sol.md) |
|  | `gpt-6-astra` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-11-13-191439-gpt-6-astra.md) |
|  | `gpt-6-luna` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-11-13-191525-gpt-6-luna.md) |
|  | `gpt-6-sol` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-11-13-191505-gpt-6-sol.md) |
|  | `gpt-6.1-sol` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-11-13-191544-gpt-6.1-sol.md) |
|  | `gpt-6.1-terra` | 2026-11-13 | 3/0/2 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-11-13-191606-gpt-6.1-terra.md) |
|  | `gpt-5.6-luna` | 2026-10-07 | 11/1/0 | M6 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-10-07-181518-gpt-5.6-luna.md) |
|  | `gpt-5.6-terra` | 2026-10-07 | 11/1/0 | M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openai/2026-10-07-181502-gpt-5.6-terra.md) |
| OpenRouter | `google/gemini-3.8-flash` *(défaut)* | 2026-11-13 | 4/1/0 | M15 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openrouter/2026-11-13-191426-google_gemini-3.8-flash.md) |
|  | `google/gemini-3.7-flash` | 2026-11-13 | 4/1/0 | M15 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openrouter/2026-11-13-191535-google_gemini-3.7-flash.md) |
|  | `moonshotai/kimi-k3.1-pro` | 2026-11-11 | 12/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openrouter/2026-11-11-062525-moonshotai_kimi-k3.1-pro.md) |
|  | `deepseek/deepseek-v4.1-flash` | 2026-10-07 | 4/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openrouter/2026-10-07-181417-deepseek_deepseek-v4.1-flash.md) |
|  | `openrouter/auto` | 2026-10-07 | 1/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/openrouter/2026-10-07-181412-openrouter_auto.md) |
| Qwen (Alibaba DashScope) | `qwen3.7-plus` *(défaut)* | 2026-11-13 | 5/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/qwen/2026-11-13-191430-qwen3.7-plus.md) |
|  | `qwen3.8-flash` | 2026-11-13 | 5/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/qwen/2026-11-13-191623-qwen3.8-flash.md) |
|  | `qwen3.8-max` | 2026-11-13 | 5/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/qwen/2026-11-13-191724-qwen3.8-max.md) |
|  | `deepseek-v4.1-flash-1017` | 2026-11-11 | 12/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/qwen/2026-11-11-062650-deepseek-v4.1-flash-1017.md) |
|  | `qwen3.7-flash` | 2026-10-07 | 11/1/0 | M2 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/qwen/2026-10-07-181525-qwen3.7-flash.md) |
|  | `qwen3.7-max` | 2026-10-07 | 11/1/0 | M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/qwen/2026-10-07-181412-qwen3.7-max.md) |
| Together AI | `zai-org/GLM-5.3-Flash` *(défaut)* | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/together/2026-11-13-191423-zai-org_GLM-5.3-Flash.md) |
|  | `Qwen/Qwen3.5-9B` | 2026-11-13 | 1/0/4 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/together/2026-11-13-191431-Qwen_Qwen3.5-9B.md) |
|  | `meta-llama/Llama-3.3-70B-Instruct-Turbo` | 2026-11-11 | 2/9/1 | M1, M2, M3, M4, M5, M6, M8, M9, M10 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/together/2026-11-11-061047-meta-llama_Llama-3.3-70B-Instruct-Turbo.md) |
|  | `Qwen/Qwen3-VL-8B-Instruct` | 2026-08-30 | 0/1/0 | M9 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/together/2026-08-30-124627-Qwen_Qwen3-VL-8B-Instruct.md) |
| TypeSafe (Jev) | `jev-latest` *(défaut)* | 2026-11-11 | 8/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/typesafe/2026-11-11-074345-jev-latest.md) |
|  | `jev-preview` | 2026-11-11 | 8/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/typesafe/2026-11-11-074346-jev-preview.md) |
| Z.AI (Zhipu GLM) | `glm-5.3-flashx` *(défaut)* | 2026-11-13 | 3/1/1 | M16 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/zai/2026-11-13-191424-glm-5.3-flashx.md) |
|  | `glm-5.2` | 2026-11-13 | 4/0/1 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/zai/2026-11-13-191445-glm-5.2.md) |
|  | `glm-5.3` | 2026-11-13 | 3/1/1 | M16 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/zai/2026-11-13-191722-glm-5.3.md) |
|  | `glm-5.3-flash` | 2026-11-13 | 3/1/1 | M16 | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/zai/2026-11-13-191752-glm-5.3-flash.md) |
|  | `glm-4.6v-flash` | 2026-11-11 | 1/0/0 | — | [rapport](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/zai/2026-11-11-074429-glm-4.6v-flash.md) |

## Raisonnement par modèle

Les modes M15 (bascule du raisonnement), M16 (balayage des efforts) et M17 (budget) sont décrits dans le
[README du kit](https://github.com/Orkeon/orkeon/blob/main/llmproviders-test/README.md#the-protocol-modes). ✅ la valeur est partie telle quelle,
`→low` Orkeon l'a abaissée à la valeur la plus proche que le modèle accepte, ➖ rien n'a été envoyé,
❌ le fournisseur a refusé ce qu'Orkeon a envoyé, vide l'étape n'a pas été exécutée. `Autres` liste les
valeurs propres au fournisseur ; `Trace` est ✅ lorsque le provider Orkeon a restitué une trace de raisonnement, `wire only` lorsque le fournisseur l'a envoyée et que le provider l'a perdue.

| Fournisseur | Modèle | Date | on | off | low | medium | high | xhigh | max | budget | Autres | Trace |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Anthropic | `claude-fable-5-1` | 2026-11-13 | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ➖ |  |  |
|  | `claude-fable-5-2` | 2026-11-13 | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ➖ |  |  |
|  | `claude-haiku-5-5` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ➖ |  |  |
|  | `claude-opus-5-5` | 2026-11-13 | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ➖ |  |  |
|  | `claude-sonnet-5` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ➖ |  |  |
|  | `claude-sonnet-5-5` | 2026-11-13 | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ➖ |  |  |
| DeepSeek | `deepseek-flash` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | →high | ✅ | ➖ |  | ✅ |
| Google Gemini | `gemini-3.7-flash` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  |  |
|  | `gemini-3.8-flash` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  |  |
|  | `gemini-4-argon` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  |  |
|  | `gemini-4-carbon` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  |  |
| Grok (x.AI) | `grok-4.6` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  | ✅ |
|  | `grok-4.7` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  | ✅ |
| Kimi (Moonshot AI) | `kimi-k2.6` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | →high | →high | ➖ |  | ✅ |
|  | `kimi-k2.7-code` | 2026-11-13 | ✅ | ❌ | ✅ | ✅ | ✅ | →high | →high | ➖ |  | ✅ |
|  | `kimi-k3` | 2026-11-13 | ➖ | ➖ | ✅ | ✅ | ✅ | →high | →high | ➖ |  | ✅ |
|  | `kimi-k3.1-pro` | 2026-11-13 | ➖ | ➖ | ✅ | ✅ | ✅ | →high | →high | ➖ |  | ✅ |
| Mistral AI | `mistral-large-4` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  | ✅ |
|  | `mistral-medium-2604` | 2026-11-13 |  |  | ❌ | ❌ | ✅ | →high | →high | ➖ |  | ✅ |
|  | `mistral-small-2603` | 2026-11-13 |  |  | ❌ | ❌ | ✅ | →high | →high | ➖ |  | ✅ |
| OpenAI | `gpt-5.6-sol` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ | none ✅ |  |
|  | `gpt-6-astra` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  |  |
|  | `gpt-6-luna` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ | none ✅ |  |
|  | `gpt-6-sol` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ | none ✅ |  |
|  | `gpt-6.1-sol` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ |  |  |
|  | `gpt-6.1-terra` | 2026-11-13 |  |  | ✅ | ✅ | ✅ | →high | →high | ➖ | none ✅ |  |
| OpenRouter | `google/gemini-3.7-flash` | 2026-11-13 | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |  | ✅ |
|  | `google/gemini-3.8-flash` | 2026-11-13 | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |  | ✅ |
| Qwen (Alibaba DashScope) | `qwen3.7-plus` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | →high | →high | ✅ |  | ✅ |
|  | `qwen3.8-flash` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | →high | →high | ✅ |  | ✅ |
|  | `qwen3.8-max` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | →high | →high | ✅ |  | ✅ |
| Z.AI (Zhipu GLM) | `glm-5.2` | 2026-11-13 | ✅ | ✅ | ✅ | ✅ | ✅ | →high | ✅ | ➖ |  | ✅ |
|  | `glm-5.3` | 2026-11-13 | ✅ | ➖ | ✅ | ❌ | ✅ | →high | ✅ | ➖ |  | ✅ |
|  | `glm-5.3-flash` | 2026-11-13 | ✅ | ➖ | ✅ | ❌ | ✅ | →high | ✅ | ➖ |  | ✅ |
|  | `glm-5.3-flashx` | 2026-11-13 | ✅ | ➖ | ✅ | ❌ | ✅ | →high | ✅ | ➖ |  | ✅ |

## Matrice brute du fournisseur

M18 envoie chaque fragment de la liste `rawReasoning` du catalogue au fournisseur tel quel, sans
passer par le jugement d'Orkeon sur ce qu'un modèle accepte : le tableau montre ce que le
fournisseur lui-même accepte, d'après le dernier rapport du modèle qui a exécuté M18. « Acceptés »
signifie HTTP 2xx, pas que la valeur ait eu un effet : comparer les traces et les jetons de
complétion dans les rapports. `Trace` liste les fragments après lesquels une trace de
raisonnement est revenue dans la réponse du fournisseur, lue sur le fil.

| Fournisseur | Modèle | Date | Acceptés | Refusés | Trace |
|---|---|---|---|---|---|
| Anthropic | `claude-fable-5-1` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `trace:summarized` | `off:disabled@high`, `off:disabled@xhigh`, `off:between_tools@high`, `off:between_tools@xhigh`, `budget:1024` |  |
|  | `claude-fable-5-2` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `trace:summarized` | `off:disabled@high`, `off:disabled@xhigh`, `off:between_tools@high`, `off:between_tools@xhigh`, `budget:1024` | `trace:summarized` |
|  | `claude-haiku-5-5` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `trace:summarized`, `off:disabled@high` | `off:disabled@xhigh`, `off:between_tools@high`, `off:between_tools@xhigh`, `budget:1024` | `trace:summarized` |
|  | `claude-opus-5-5` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `trace:summarized` | `off:disabled@high`, `off:disabled@xhigh`, `off:between_tools@high`, `off:between_tools@xhigh`, `budget:1024` | `trace:summarized` |
|  | `claude-sonnet-5` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `trace:summarized`, `off:disabled@high`, `off:disabled@xhigh` | `off:between_tools@high`, `off:between_tools@xhigh`, `budget:1024` | `trace:summarized` |
|  | `claude-sonnet-5-5` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `trace:summarized`, `off:between_tools@high` | `off:disabled@high`, `off:disabled@xhigh`, `off:between_tools@xhigh`, `budget:1024` |  |
| DeepSeek | `deepseek-flash` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
| Google Gemini | `gemini-3.7-flash` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high` | `effort:minimal`, `effort:xhigh`, `effort:max` |  |
|  | `gemini-3.8-flash` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high` | `effort:minimal`, `effort:xhigh`, `effort:max` |  |
|  | `gemini-4-argon` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high` | `effort:minimal`, `effort:xhigh`, `effort:max` |  |
|  | `gemini-4-carbon` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high` | `effort:minimal`, `effort:xhigh`, `effort:max` |  |
| Grok (x.AI) | `grok-4.6` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:none`, `effort:max` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` |
|  | `grok-4.7` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:none`, `effort:max` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` |
| HuggingFace | `Qwen/Qwen3.5-9B` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
|  | `meta-llama/Llama-3.1-8B-Instruct` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  |  |
| Kimi (Moonshot AI) | `kimi-k2.6` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
|  | `kimi-k2.7-code` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` | `effort:none`, `toggle:off` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
|  | `kimi-k3` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
|  | `kimi-k3.1-pro` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
| Mammouth AI | `gemini-3.7-flash` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max` | `effort:none` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max` |
|  | `gemini-3.8-flash` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max` | `effort:none` | `effort:minimal`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max` |
| MiniMax | `MiniMax-M2` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |
|  | `MiniMax-M3` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:off` | `toggle:on` | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max` |
|  | `MiniMax-M3.1-Flash` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` | `effort:none`, `toggle:off` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
| Mistral AI | `mistral-large-4` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high` | `effort:none`, `effort:minimal`, `effort:xhigh`, `effort:max` | `effort:low`, `effort:medium`, `effort:high` |
|  | `mistral-medium-2604` | 2026-11-13 | `effort:none`, `effort:high` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:xhigh`, `effort:max` | `effort:high` |
|  | `mistral-small-2603` | 2026-11-13 | `effort:none`, `effort:high` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:xhigh`, `effort:max` | `effort:high` |
| OpenAI | `gpt-5.6-sol` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:minimal`, `effort:max` |  |
|  | `gpt-6-astra` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:none`, `effort:minimal`, `effort:max` |  |
|  | `gpt-6-luna` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:minimal`, `effort:max` |  |
|  | `gpt-6-sol` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:minimal`, `effort:max` |  |
|  | `gpt-6.1-sol` | 2026-11-13 | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:none`, `effort:minimal`, `effort:max` |  |
|  | `gpt-6.1-terra` | 2026-11-13 | `effort:none`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh` | `effort:minimal`, `effort:max` |  |
| OpenRouter | `google/gemini-3.7-flash` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `budget:1024` | `effort:none`, `toggle:off` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `budget:1024` |
|  | `google/gemini-3.8-flash` | 2026-11-13 | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `budget:1024` | `effort:none`, `toggle:off` | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `budget:1024` |
| Qwen (Alibaba DashScope) | `qwen3.7-plus` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `toggle:on`, `toggle:off`, `budget:1024` | `effort:max` | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `toggle:on`, `budget:1024` |
|  | `qwen3.8-flash` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off`, `budget:1024` |  | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `budget:1024` |
|  | `qwen3.8-max` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off`, `budget:1024` |  | `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `budget:1024` |
| Together AI | `Qwen/Qwen3.5-9B` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
|  | `zai-org/GLM-5.3-Flash` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |
| Z.AI (Zhipu GLM) | `glm-5.2` | 2026-11-13 | `effort:none`, `effort:minimal`, `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on`, `toggle:off` |  | `effort:low`, `effort:medium`, `effort:high`, `effort:xhigh`, `effort:max`, `toggle:on` |
|  | `glm-5.3` | 2026-11-13 | `effort:low`, `effort:high`, `effort:max`, `toggle:on` | `effort:none`, `effort:minimal`, `effort:medium`, `effort:xhigh`, `toggle:off` | `effort:low`, `effort:high`, `effort:max`, `toggle:on` |
|  | `glm-5.3-flash` | 2026-11-13 | `effort:low`, `effort:high`, `effort:max`, `toggle:on` | `effort:none`, `effort:minimal`, `effort:medium`, `effort:xhigh`, `toggle:off` | `effort:low`, `effort:high`, `effort:max`, `toggle:on` |
|  | `glm-5.3-flashx` | 2026-11-13 | `effort:low`, `effort:high`, `effort:max`, `toggle:on` | `effort:none`, `effort:minimal`, `effort:medium`, `effort:xhigh`, `toggle:off` | `effort:low`, `effort:high`, `effort:max`, `toggle:on` |
