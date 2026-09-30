> 🇬🇧 [English version](../../guides/multimodal.md)

# Contenu multi-modal (vision)

> Depuis le chantier R3.9, la vision est **réelle** : les contenus image circulent de
> bout en bout, des abstractions `MultiModalContent` (Domain) jusqu'aux payloads des
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
provider compatible OpenAI (Azure OpenAI, DeepSeek, Gemini, Grok, HuggingFace, Kimi, Mammouth,
MiniMax, Mistral, OpenRouter, Qwen, Together AI, Z.AI).

**Ollama (`/api/chat`)** — pas de parts de contenu : le message garde une chaîne `content`
simple et les images voyagent en chaînes base64 nues (sans préfixe `data:`, sans type de
média) :

```json
{ "role": "user", "content": "Décris ce graphique", "images": ["iVBOR..."] }
```

Un message portant une image fait passer Ollama de `/api/generate` à `/api/chat`. Ollama ne
télécharge jamais d'URL distante : une image référencée seulement par URL est ignorée avec un
avertissement structuré (`… image(s) referenced by URL`) — chargez plutôt les octets. Seuls le
texte et les images en ligne sont transmis.

## Activation (opt-in)

Le sous-système reste **opt-in** (décision R4.9) — voir
[Sous-systèmes opt-in](../reference/opt-in-subsystems.md) :

```csharp
services.AddOrkeonFileSystem(configuration);   // prérequis du loader (VFS)
services.AddOrkeonMultiModal(configuration);   // lie Orkeon:MultiModal
// ou : services.AddOrkeonMultiModal();        // options par défaut
```

Options (`Orkeon:MultiModal`, `MultiModalOptions`) : `Enabled` (true), `MaxImageSizeBytes`
(20 Mo par défaut), `SupportedImageFormats` (png, jpeg, gif, webp par défaut),
`MaxAudioDurationSeconds` (300), `SupportedAudioFormats` (wav, mp3, ogg). `AutoResizeImages`
et `MaxImageDimension` existent aussi sur la classe d'options, mais rien ne les lit encore :
aucune image n'est redimensionnée.

> L'envoi des payloads vision par les providers ne dépend **pas** de l'activation DI :
> un `LlmMessage` portant un `MultiModalContent` avec images est composé en payload
> structuré par chaque provider dont les capacités déclarent `Vision` (la base
> partagée `OpenAICompatibleProviderBase` fait la traduction une fois).
> `AddOrkeonMultiModal` active la validation (`IContentValidationService`) et le
> chargeur VFS (`IMultiModalContentLoader`).

## Providers et formats supportés

| Capacité | Supporté |
|---|---|
| Providers vision (capacité `Vision = true`) | Les 16 : Anthropic, OpenAI, Azure OpenAI, DeepSeek (natif sur `deepseek-flash` depuis V4.1 ; `deepseek-v4-pro` reste texte seul), Gemini, Grok, HuggingFace, Kimi, MiniMax (par modèle : `MiniMax-M3` voit, le `MiniMax-M2` par défaut non), Mistral, Ollama (`images` natif, par modèle), Qwen, Together AI (par modèle : `zai-org/GLM-5.3-Flash` voit, le Llama par défaut non), Z.AI (par modèle : `glm-5.3-flash` voit, le `glm-5.2` par défaut non), OpenRouter (par modèle, non campagné) et Mammouth AI (par modèle, d'après la liste `text, image` du vendeur — non campagné) |
| Modèle sans vision derrière un provider vision | Le refus du vendeur lui-même, attribué par `CapabilityMismatchHint` sur les providers compatibles OpenAI et Ollama (la capacité est déclarée par provider, la réalité est par modèle) |
| Provider sans la capacité (un `ILlmProvider` tiers qui ne déclare rien) | Dégradation vers le repli texte `LlmMessage.Content`, avec un avertissement structuré nommant les parts abandonnées |
| Types MIME image | `image/png`, `image/jpeg`, `image/gif`, `image/webp` |
| Sources d'image | Bytes bruts (base64), URL http(s), data URL base64 |
| Audio / fichiers vers les providers | Non supporté — `NotSupportedException` explicite sur Anthropic et les providers compatibles OpenAI ; Ollama ne transmet que le texte et les images en ligne |

Sur Anthropic et les providers compatibles OpenAI, un type MIME non supporté (ex.
`image/bmp`), un schéma d'URI non supporté (ex. `ftp://`) ou une part audio/fichier dans un
payload vision lèvent une exception explicite — aucune dégradation silencieuse en texte.

## Interop Microsoft.Extensions.AI

`ContentConverter.ToAIContents`/`FromAIContents` convertissent désormais les parts
image/audio/fichier vers les types natifs `DataContent` (bytes) et `UriContent` (URI)
de Microsoft.Extensions.AI (et inversement), au lieu de l'ancienne dégradation en
descriptions textuelles.

## Tests

- `ContentConverterPayloadTests` — composition des blocs Anthropic / parts OpenAI,
  chargement VFS, erreurs sur formats non supportés.
- `AnthropicVisionPayloadTests` / `OpenAIVisionPayloadTests` / `ProviderVisionPayloadTests`
  (chaque provider compatible OpenAI) / `OllamaChatEndpointTests` — vérification du JSON
  réellement émis sur le fil (handler HTTP factice).
- `MultiModalContentLoaderTests` — contraintes d'options (taille, formats, Enabled).
- `OptInSubsystemsRegistrationTests` — enregistrement DI opt-in.
