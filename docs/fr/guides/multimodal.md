> 🇬🇧 [English version](../../guides/multimodal.md)

# Contenu multi-modal (vision)

> Depuis le chantier R3.9, la vision est **réelle** : les contenus image circulent de
> bout en bout, des abstractions `MultiModalContent` (Domain) jusqu'aux payloads des
> providers Anthropic et OpenAI. Ce guide décrit le flux, les formats émis sur le fil,
> l'activation opt-in et les limites.

## Vue d'ensemble du flux

```
MultiModalContent (Domain)            LlmMessage (Domain)              Payload provider (Infrastructure)
┌──────────────────────────┐   ┌───────────────────────────────┐   ┌─────────────────────────────────────┐
│ TextContentPart          │   │ Role = "user"                 │   │ Anthropic : blocs "text"/"image"    │
│ ImageContentPart         │ → │ Content = repli texte         │ → │ OpenAI    : parts "text"/"image_url"│
│ (bytes ou URI)           │   │ MultiModalContent = parts     │   │ (via ContentConverter)              │
└──────────────────────────┘   └───────────────────────────────┘   └─────────────────────────────────────┘
```

- `MultiModalContent` (value object immuable, `Orkeon.Domain.Shared.ValueObjects.Content`)
  porte les parts texte/image/audio/fichier.
- `LlmMessage.User(MultiModalContent)` crée un message utilisateur qui transporte les
  parts **et** un repli texte (`Content = ToTextOnly()`) pour les providers sans vision.
- `ContentConverter` (`Orkeon.Infrastructure.LLMs.Converters`) compose les fragments de
  payload propres à chaque API.

## Composer et envoyer une image

```csharp
using Orkeon.Domain.Shared.ValueObjects;
using Orkeon.Domain.Shared.ValueObjects.Content;

// 1. Depuis des bytes (envoyés en base64)
var content = MultiModalContent.Empty()
    .AddText("Décris ce graphique")
    .AddImage(ImageContentPart.FromBytes(pngBytes, "image/png"));

// 2. Ou depuis une URL http(s) (le provider télécharge l'image)
var contentFromUrl = MultiModalContent.Empty()
    .AddText("Que montre cette photo ?")
    .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/photo.jpg"), "image/jpeg"));

// 3. Envoi via n'importe quel ILlmProvider vision (Anthropic, OpenAI)
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
les bytes bruts sont encodés en data URL base64.

## Activation (opt-in)

Le sous-système reste **opt-in** (décision R4.9) — voir
[Sous-systèmes opt-in](../reference/opt-in-subsystems.md) :

```csharp
services.AddOrkeonFileSystem(configuration);   // prérequis du loader (VFS)
services.AddOrkeonMultiModal(configuration);   // lie Orkeon:MultiModal
// ou : services.AddOrkeonMultiModal();        // options par défaut
```

Options (`Orkeon:MultiModal`) : `Enabled`, `MaxImageSizeBytes` (20 Mo par défaut),
`SupportedImageFormats` (png, jpeg, gif, webp par défaut), `MaxAudioDurationSeconds`,
`SupportedAudioFormats`.

> L'envoi des payloads vision par les providers ne dépend **pas** de l'activation DI :
> un `LlmMessage` portant un `MultiModalContent` avec images est toujours composé en
> payload structuré par Anthropic/OpenAI. `AddOrkeonMultiModal` active la validation
> (`IContentValidationService`) et le chargeur VFS (`IMultiModalContentLoader`).

## Providers et formats supportés

| Capacité | Supporté |
|---|---|
| Providers vision | `AnthropicLlmProvider`, `OpenAIProvider` |
| Autres providers (Groq, Mistral, DeepSeek, …) | Dégradation vers le repli texte `LlmMessage.Content` |
| Types MIME image | `image/png`, `image/jpeg`, `image/gif`, `image/webp` |
| Sources d'image | Bytes bruts (base64), URL http(s), data URL base64 |
| Audio / fichiers vers les providers | Non supporté — `NotSupportedException` explicite |

Un type MIME non supporté (ex. `image/bmp`), un schéma d'URI non supporté (ex. `ftp://`)
ou une part audio/fichier dans un payload vision lèvent une exception explicite —
aucune dégradation silencieuse en texte.

## Interop Microsoft.Extensions.AI

`ContentConverter.ToAIContents`/`FromAIContents` convertissent désormais les parts
image/audio/fichier vers les types natifs `DataContent` (bytes) et `UriContent` (URI)
de Microsoft.Extensions.AI (et inversement), au lieu de l'ancienne dégradation en
descriptions textuelles.

## Tests

- `ContentConverterPayloadTests` — composition des blocs Anthropic / parts OpenAI,
  chargement VFS, erreurs sur formats non supportés.
- `AnthropicVisionPayloadTests` / `OpenAIVisionPayloadTests` — vérification du JSON
  réellement émis sur le fil (handler HTTP factice).
- `MultiModalContentLoaderTests` — contraintes d'options (taille, formats, Enabled).
- `OptInSubsystemsRegistrationTests` — enregistrement DI opt-in.
