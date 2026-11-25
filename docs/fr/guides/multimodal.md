> 🇬🇧 [English version](../../guides/multimodal.md)

# Contenu multi-modal (vision)

> Les contenus image circulent de bout en bout, des abstractions `MultiModalContent` (Domain) jusqu'aux payloads des
> providers — blocs de contenu Anthropic, parts de contenu OpenAI (écrites une seule fois pour
> les quatorze providers compatibles OpenAI) et tableau `images` d'Ollama. Ce guide décrit le
> flux, les formats émis sur le fil, l'activation opt-in et les limites.

## Vue d'ensemble du flux

```
MultiModalContent (Domain)            LlmMessage (Domain)              Payload provider (Infrastructure)
┌──────────────────────────┐   ┌───────────────────────────────┐   ┌─────────────────────────────────────┐
│ TextContentPart          │   │ Role = "user"                 │   │ Anthropic : blocs "text"/"image"    │
│ ImageContentPart         │ → │ Content = repli texte         │ → │ OpenAI    : parts "text"/"image_url"│
│ (bytes ou URI)           │   │ MultiModalContent = parts     │   │ Ollama    : content + "images"      │
└──────────────────────────┘   └───────────────────────────────┘   └─────────────────────────────────────┘
```

- `MultiModalContent` (value object immuable, `Orkeon.Domain.SharedKernel.ValueObjects.Content`)
  porte les parts texte/image/audio/fichier.
- `LlmMessage.User(MultiModalContent)` crée un message utilisateur qui transporte les
  parts **et** un repli texte (`Content = ToTextOnly()`) pour les providers sans vision.
- `ContentConverter` (`Orkeon.Infrastructure.LLMs.Converters`) compose les fragments de
  payload propres à chaque API (`ToAnthropicContentBlocks`, `ToOpenAIContentParts`,
  `ToOllamaMessage`).

## Composer et envoyer une image

```csharp
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;

// 1. Depuis des bytes (envoyés en base64)
var content = MultiModalContent.Empty()
    .AddText("Décris ce graphique")
    .AddImage(ImageContentPart.FromBytes(pngBytes, "image/png"));

// 2. Ou depuis une URL http(s) (le vendeur télécharge l'image — pas sur Ollama, voir plus bas)
var contentFromUrl = MultiModalContent.Empty()
    .AddText("Que montre cette photo ?")
    .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/photo.jpg"), "image/jpeg"));

// 3. Envoi via n'importe quel ILlmProvider qui déclare Vision
var response = await provider.ChatAsync([LlmMessage.User(content)]);
```

## Charger une image depuis le VFS (`IMultiModalContentLoader`)

Le chargeur lit le fichier via `IFileSystemService` (chemins virtuels, droits audités),
infère le type MIME de l'extension et valide les contraintes configurées
(`Orkeon:MultiModal` — taille max, formats autorisés) :

```csharp
var loader = provider.GetRequiredService<IMultiModalContentLoader>();
var image = await loader.LoadImageAsync("/workspace/chart.png");

var content = MultiModalContent.Empty()
    .AddText("Analyse ce graphique")
    .AddImage(image);
```

Erreurs claires garanties :

| Situation | Exception |
|---|---|
| Extension non supportée (`.bmp`, `.tiff`, …) | `NotSupportedException` (liste les extensions supportées) |
| Fichier introuvable | `FileNotFoundException` |
| Hors mount / droits insuffisants | `FileAccessDeniedException` |
| Taille > `MaxImageSizeBytes` ou format exclu par les options | `InvalidOperationException` |
| `Orkeon:MultiModal:Enabled = false` | `InvalidOperationException` |

## Formats émis sur le fil

**Anthropic (API Messages)** — blocs de contenu :

```json
{
  "role": "user",
  "content": [
    { "type": "text", "text": "Décris ce graphique" },
    { "type": "image", "source": { "type": "base64", "media_type": "image/png", "data": "iVBOR..." } }
  ]
}
```

Les images par URL http(s) utilisent `"source": {"type": "url", "url": "https://..."}`.
Les data URLs base64 (`data:image/png;base64,...`) sont dépaquetées en source `base64`.

**OpenAI (Chat Completions)** — parts de contenu :

```json
{
  "role": "user",
  "content": [
    { "type": "text", "text": "Décris ce graphique" },
    { "type": "image_url", "image_url": { "url": "data:image/png;base64,iVBOR..." } }
  ]
}
```

Les images par URL http(s) ou data URL passent telles quelles dans `image_url.url` ;
les bytes bruts sont encodés en data URL base64. Les mêmes parts sont écrites par chaque
provider compatible OpenAI (OpenAI, Azure OpenAI, DeepSeek, Gemini, Grok, HuggingFace, Kimi, Mammouth,
MiniMax, Mistral, OpenRouter, Qwen, Together AI, Z.AI).

**Ollama (`/api/chat`)** — pas de parts de contenu : le message garde une chaîne `content`
simple et les images voyagent en chaînes base64 nues (sans préfixe `data:`, sans type de
média) :

```json
{ "role": "user", "content": "Décris ce graphique", "images": ["iVBOR..."] }
```

Un message portant une part non textuelle fait passer Ollama de `/api/generate` à `/api/chat`.
Ollama ne télécharge jamais d'URL distante : une image référencée seulement par URL est ignorée
avec un avertissement structuré (`… image(s) referenced by URL`) — chargez plutôt les octets.
Ollama ne prend ni audio ni fichier : une telle part lève `NotSupportedException` avant tout
envoi, comme chez Anthropic et les providers compatibles OpenAI.

## Activation (opt-in)

Le sous-système reste **opt-in** — voir
[Sous-systèmes opt-in](../reference/opt-in-subsystems.md) :

```csharp
services.AddOrkeonFileSystem(configuration);   // prérequis du loader (VFS)
services.AddOrkeonMultiModal(configuration);   // lie Orkeon:MultiModal
// ou : services.AddOrkeonMultiModal();        // options par défaut
```

Options (`Orkeon:MultiModal`, `MultiModalOptions`) : `Enabled` (true), `MaxImageSizeBytes`
(20 Mo par défaut), `MaxImages` (10 par appel pour les [images d'un run](#les-images-dun-run)), `SupportedImageFormats` (png, jpeg, gif, webp par défaut),
`MaxAudioDurationSeconds` (300), `SupportedAudioFormats` (wav, mp3, ogg). Aucune image n'est
redimensionnée : une image au-delà de la taille limite échoue à la validation.

> L'envoi des payloads vision par les providers ne dépend **pas** de l'activation DI :
> un `LlmMessage` portant un `MultiModalContent` avec images est composé en payload
> structuré par chaque provider dont les capacités déclarent `Vision` (la base
> partagée `OpenAICompatibleProviderBase` fait la traduction une fois).
> `AddOrkeonMultiModal` active la validation (`IContentValidationService`) et le
> chargeur VFS (`IMultiModalContentLoader`).

## Les images d'un run

`CrewInput.Images` nomme par chemin virtuel les images venues avec le message auquel un crew répond ; rien n'est
lu ni copié à ce moment. Chaque tâche du run les voit dans son premier tour utilisateur :

- **Quand.** À chaque appel au modèle d'une tâche, les octets sont relus par le système de fichiers du run et
  rejoignent ce seul appel. Ils ne sont dans aucun message que la boucle garde : une compaction, un checkpoint,
  l'historique des runs, les traces et les journaux tiennent le chemin. Une cassette en met l'empreinte dans la
  clé de l'appel : rejouer avec une autre image ne trouve rien. Une tâche reprise avec d'autres images,
  ou un autre fichier sous le même chemin, est refaite.
- **À quel modèle.** Celui qui répond, après le routage et à chaque maillon d'un repli. Il doit être connu pour
  lire les images (`ModelCapabilities.Vision`) ; un modèle inconnu ne l'est pas.
- **Dans quelles bornes.** `Orkeon:MultiModal` : `Enabled`, `MaxImages`, `MaxImageSizeBytes` et
  `SupportedImageFormats`. Le type se lit sur les octets — png, jpeg, webp et gif —, jamais sur le nom.
- **Dans les bornes de son vendeur.** Sous `MaxImageSizeBytes`, ce que la page du vendeur énonce, lu avant l'appel :

  | Provider | Borne | Source |
  |---|---|---|
  | `anthropic` | 10 Mo en base64, 8000 x 8000 pixels ; requête de 128 Mo | [Vision](https://platform.claude.com/docs/en/build-with-claude/vision) |
  | `azure-openai` | 20 Mo | [Use vision-enabled chat models](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/gpt-with-vision) |
  | `gemini` | requête de 20 Mo (l'[autre page](https://aistudio.google.com/docs/file-input-methods) dit 100 Mo : la plus basse) ; png, jpeg, webp | [Image understanding](https://aistudio.google.com/docs/image-understanding) |
  | `grok` | 20 Mio ; png, jpeg | [Image understanding](https://docs.x.ai/docs/guides/image-understanding) |
  | `kimi` | requête de 100 Mo | [Vision model](https://platform.kimi.ai/docs/guide/use-kimi-vision-model) |
  | `minimax` | 10 Mo, côtés jusqu'à 200:1 ; requête de 64 Mo | [Chat](https://platform.minimax.io/docs/api-reference/text-chat-openai) |
  | `mistral` | 10 Mo, 8 images, 10000 x 10000 pixels (les [limites connues](https://docs.mistral.ai/resources/known-limitations) disent 20 Mo : la plus basse) ; png, jpeg, webp (seul un gif d'une image est pris : Orkeon n'en envoie pas) | [Vision](https://docs.mistral.ai/inference/conversations/vision) |
  | `openai` | requête de 512 Mo | [Images and vision](https://developers.openai.com/api/docs/guides/images-vision) |
  | `qwen` | 50 Mio en base64, préfixe de data URI compris, côtés de plus de 10 pixels et jusqu'à 200:1, 7680 x 4320 pixels ; requête de 16 Mio sur le domaine DashScope, celui d'Orkeon par défaut ; png, jpeg, et webp jusqu'à 3840 x 2160 | [Vision](https://www.alibabacloud.com/help/en/model-studio/vision) |
  | `zai` | moins de 5 Mo, 6000 x 6000 pixels ; png, jpeg | [Chat completion](https://docs.z.ai/api-reference/llm/chat-completion) |
  | tout autre | 5 Mo en base64 | aucune trouvée : celle d'Orkeon, prudente |

  Un mégaoctet vaut un million d'octets là où la page n'écrit pas MiB, et une requête en garde un pour son texte. Les
  côtés se lisent dans l'en-tête du fichier.
- **Quand le vendeur refuse quand même.** Un 400, 413, 415 ou 422 à un appel qui porte des images est redemandé une fois
  sans elles, chacune nommée comme non vue. Tout autre statut, une conversation trop longue, un compte épuisé et une mauvaise
  clé gardent leur traitement, de même qu'un flux qui a déjà dit quelque chose. Quand le second appel échoue aussi, l'erreur
  est la première, sauf si la seconde est de celles qu'un repli prend en charge. Le run compte un appel, celui de
  la réponse ; une cassette enregistre le refus et le second appel, chacun sous sa clé, et rejoue les deux.
  Une fois le second appel répondu, ces images ne sont plus montrées à ce modèle dans le run. Les données qu'un vendeur
  cite dans son erreur ne sont ni dans l'erreur ni dans le journal.
- **À quel agent.** Celui dont le `filesystem:` exclut le chemin ne voit pas l'image.

Une image qui n'est pas montrée n'est jamais perdue en silence : sa place dans le tour le dit par son chemin, et
`CrewOutput.UnseenImages` donne le chemin, qui ne l'a pas vue et pourquoi. La boucle texte, une tâche de décision
et un provider que l'hôte enregistre hors de la fabrique (`AddOrkeonLlmProvider(_ => ...)`) ne portent aucune
image, et le disent de même. Un compte de tokens fait avant un appel ne compte pas les images ; l'usage que le
vendeur rapporte, si.

## Providers et formats supportés

| Capacité | Supporté |
|---|---|
| Providers vision (capacité `Vision = true`) | Les 16 : Anthropic, OpenAI, Azure OpenAI, DeepSeek (natif sur `deepseek-flash` depuis V4.1 ; `deepseek-v4-pro` reste texte seul), Gemini, Grok, HuggingFace, Kimi, MiniMax (par modèle : le `MiniMax-M3` par défaut voit, `MiniMax-M2` non), Mistral, Ollama (`images` natif, par modèle), Qwen, Together AI (par modèle : le `zai-org/GLM-5.3-Flash` par défaut voit), Z.AI (par modèle : le `glm-5.3-flashx` par défaut et `glm-5.3-flash` voient, `glm-5.2` non), OpenRouter (par modèle) et Mammouth AI (par modèle, d'après la liste `text, image` du vendeur) |
| Modèle sans vision derrière un provider vision | Le refus du vendeur lui-même, attribué par `CapabilityMismatchHint` sur les providers compatibles OpenAI et Ollama (la capacité est déclarée par provider, la réalité est par modèle) |
| Provider sans la capacité (un `ILlmProvider` tiers qui ne déclare rien) | Dégradation vers le repli texte `LlmMessage.Content`, avec un avertissement structuré nommant les parts abandonnées |
| Types MIME image | `image/png`, `image/jpeg`, `image/gif`, `image/webp` |
| Sources d'image | Bytes bruts (base64), URL http(s), data URL base64 |
| Audio / fichiers vers les providers | Non supporté — `NotSupportedException` explicite sur Anthropic, les providers compatibles OpenAI et Ollama |

Sur Anthropic et les providers compatibles OpenAI, un type MIME non supporté (ex.
`image/bmp`), un schéma d'URI non supporté (ex. `ftp://`) ou une part audio/fichier dans un
payload vision lèvent une exception explicite — aucune dégradation silencieuse en texte.
Ollama refuse une part audio/fichier de la même façon.

## Interop Microsoft.Extensions.AI

Orkeon convertit les parts image/audio/fichier vers les types natifs de
Microsoft.Extensions.AI `DataContent` (octets) et `UriContent` (URI), et inversement, au sein
d'`Orkeon.Infrastructure` ; le convertisseur est interne, ce n'est pas une API à appeler.

## Tests

- `ContentConverterPayloadTests` — composition des blocs Anthropic / parts OpenAI,
  chargement VFS, erreurs sur formats non supportés.
- `AnthropicVisionPayloadTests` / `OpenAIVisionPayloadTests` / `ProviderVisionPayloadTests`
  (chaque provider compatible OpenAI) / `OllamaChatEndpointTests` — vérification du JSON
  réellement émis sur le fil (handler HTTP factice).
- `MultiModalContentLoaderTests` — contraintes d'options (taille, formats, Enabled).
- `OptInSubsystemsRegistrationTests` — enregistrement DI opt-in.
