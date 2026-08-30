> 🇫🇷 [Version française](../fr/guides/multimodal.md)

# Multi-modal content (vision)

> Since the R3.9 work item, vision is **real**: image content flows end to end, from the
> `MultiModalContent` abstractions (Domain) down to the Anthropic and OpenAI provider
> payloads. This guide describes the flow, the formats emitted on the wire, the opt-in
> activation and the limits.

## Flow overview

```
MultiModalContent (Domain)            LlmMessage (Domain)              Payload provider (Infrastructure)
┌──────────────────────────┐   ┌───────────────────────────────┐   ┌─────────────────────────────────────┐
│ TextContentPart          │   │ Role = "user"                 │   │ Anthropic : blocs "text"/"image"    │
│ ImageContentPart         │ → │ Content = repli texte         │ → │ OpenAI    : parts "text"/"image_url"│
│ (bytes ou URI)           │   │ MultiModalContent = parts     │   │ (via ContentConverter)              │
└──────────────────────────┘   └───────────────────────────────┘   └─────────────────────────────────────┘
```

- `MultiModalContent` (immutable value object, `Orkeon.Domain.SharedKernel.ValueObjects.Content`)
  carries the text/image/audio/file parts.
- `LlmMessage.User(MultiModalContent)` creates a user message that transports the
  parts **and** a text fallback (`Content = ToTextOnly()`) for providers without vision.
- `ContentConverter` (`Orkeon.Infrastructure.LLMs.Converters`) composes the payload
  fragments specific to each API.

## Composing and sending an image

```csharp
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;

// 1. From bytes (sent as base64)
var content = MultiModalContent.Empty()
    .AddText("Describe this chart")
    .AddImage(ImageContentPart.FromBytes(pngBytes, "image/png"));

// 2. Or from an http(s) URL (the provider downloads the image)
var contentFromUrl = MultiModalContent.Empty()
    .AddText("What does this photo show?")
    .AddImage(ImageContentPart.FromUri(new Uri("https://example.com/photo.jpg"), "image/jpeg"));

// 3. Send via any vision ILlmProvider (Anthropic, OpenAI)
var response = await provider.ChatAsync([LlmMessage.User(content)]);
```

## Loading an image from the VFS (`IMultiModalContentLoader`)

The loader reads the file via `IFileSystemService` (virtual paths, audited rights),
infers the MIME type from the extension and validates the configured constraints
(`Orkeon:MultiModal` — max size, allowed formats):

```csharp
var loader = provider.GetRequiredService<IMultiModalContentLoader>();
var image = await loader.LoadImageAsync("/workspace/chart.png");

var content = MultiModalContent.Empty()
    .AddText("Analyze this chart")
    .AddImage(image);
```

Clear errors guaranteed:

| Situation | Exception |
|---|---|
| Unsupported extension (`.bmp`, `.tiff`, …) | `NotSupportedException` (lists the supported extensions) |
| File not found | `FileNotFoundException` |
| Outside a mount / insufficient rights | `FileAccessDeniedException` |
| Size > `MaxImageSizeBytes` or format excluded by the options | `InvalidOperationException` |
| `Orkeon:MultiModal:Enabled = false` | `InvalidOperationException` |

## Formats emitted on the wire

**Anthropic (Messages API)** — content blocks:

```json
{
  "role": "user",
  "content": [
    { "type": "text", "text": "Describe this chart" },
    { "type": "image", "source": { "type": "base64", "media_type": "image/png", "data": "iVBOR..." } }
  ]
}
```

Images by http(s) URL use `"source": {"type": "url", "url": "https://..."}`.
Base64 data URLs (`data:image/png;base64,...`) are unwrapped into a `base64` source.

**OpenAI (Chat Completions)** — content parts:

```json
{
  "role": "user",
  "content": [
    { "type": "text", "text": "Describe this chart" },
    { "type": "image_url", "image_url": { "url": "data:image/png;base64,iVBOR..." } }
  ]
}
```

Images by http(s) URL or data URL pass through as-is in `image_url.url`;
raw bytes are encoded as a base64 data URL.

## Activation (opt-in)

The subsystem remains **opt-in** (decision R4.9) — see
[Opt-in subsystems](../reference/opt-in-subsystems.md):

```csharp
services.AddOrkeonFileSystem(configuration);   // loader prerequisite (VFS)
services.AddOrkeonMultiModal(configuration);   // binds Orkeon:MultiModal
// or: services.AddOrkeonMultiModal();         // default options
```

Options (`Orkeon:MultiModal`): `Enabled`, `MaxImageSizeBytes` (20 MB by default),
`SupportedImageFormats` (png, jpeg, gif, webp by default), `MaxAudioDurationSeconds`,
`SupportedAudioFormats`.

> Sending vision payloads from the providers does **not** depend on the DI activation:
> an `LlmMessage` carrying a `MultiModalContent` with images is composed into a
> structured payload by every provider whose capabilities declare `Vision` (the shared
> `OpenAICompatibleProviderBase` does the translation once). `AddOrkeonMultiModal`
> enables the validation (`IContentValidationService`) and the VFS loader
> (`IMultiModalContentLoader`).

## Supported providers and formats

| Capability | Supported |
|---|---|
| Vision providers (capability `Vision = true`) | All 13: Anthropic, OpenAI, Azure OpenAI, DeepSeek (per model, `deepseek-v4-flash-vision-exp`), Gemini, Grok, HuggingFace, Kimi, Mistral, Ollama (native `images`), Qwen, TogetherAI, Z.AI |
| Provider without the capability (DeepSeek) | Degradation to the `LlmMessage.Content` text fallback |
| Image MIME types | `image/png`, `image/jpeg`, `image/gif`, `image/webp` |
| Image sources | Raw bytes (base64), http(s) URL, base64 data URL |
| Audio / files to the providers | Not supported — explicit `NotSupportedException` |

An unsupported MIME type (e.g. `image/bmp`), an unsupported URI scheme (e.g. `ftp://`)
or an audio/file part in a vision payload raise an explicit exception —
no silent degradation to text.

## Microsoft.Extensions.AI interop

`ContentConverter.ToAIContents`/`FromAIContents` now convert the image/audio/file
parts to the native Microsoft.Extensions.AI types `DataContent` (bytes) and
`UriContent` (URI) (and back), instead of the former degradation into textual
descriptions.

## Tests

- `ContentConverterPayloadTests` — composition of Anthropic blocks / OpenAI parts,
  VFS loading, errors on unsupported formats.
- `AnthropicVisionPayloadTests` / `OpenAIVisionPayloadTests` — verification of the JSON
  actually emitted on the wire (fake HTTP handler).
- `MultiModalContentLoaderTests` — option constraints (size, formats, Enabled).
- `OptInSubsystemsRegistrationTests` — opt-in DI registration.
