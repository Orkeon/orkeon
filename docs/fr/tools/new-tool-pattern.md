> 🇬🇧 [English version](../../tools/new-tool-pattern.md)

> **Voir aussi** : [Inventaire des outils](./inventory.md) · [Retour à l'index](../INDEX.md)

# Pattern — Implémenter un nouvel outil

Ce guide détaille le processus complet de création d'un outil Orkeon, basé sur les classes et interfaces existantes dans le code source.

## Étape 1 — Choisir la classe de base

Orkeon fournit trois classes de base dans `Orkeon.Tools.Abstractions.Base` :

| Classe de base | Usage | Protection intégrée |
|----------------|-------|-------------------|
| `ToolBase<TRequest, TResponse>` | Outil générique | Aucune spécialisation |
| `FileToolBase<TRequest, TResponse>` | Opérations sur fichiers | `IFileSystemService` (le VFS — premier argument de ctor obligatoire) + `IPathValidator` (protection path traversal) |
| `HttpToolBase<TRequest, TResponse>` | Opérations HTTP/API | `IUrlValidator` (protection SSRF), `HttpHeaderSanitizer` |

La classe de base `ToolBase<TRequest, TResponse>` hérite de `ToolBase` (non-generic) qui implémente `IBaseTool` et `ITool` (`Orkeon.Domain.Tools`).

## Étape 2 — Définir les types Request et Response

Chaque outil typé nécessite un record `TRequest` (paramètres d'entrée) et un record `TResponse` (résultat). Les attributs `[FieldSchema]` et `[ReturnSchema]` (`Orkeon.Domain.Attributes`) servent à générer automatiquement le schéma JSON exposé au LLM.

### Attribut `[FieldSchema]` (sur TRequest)

Propriétés disponibles :

```csharp
[FieldSchema(
    Description = "...",       // Description lisible pour le LLM
    Type = "string",           // Type JSON Schema (inféré si omis)
    Format = "uri",            // Format OpenAPI (inféré si omis)
    IsRequired = true,         // Required (inféré de la nullabilité C# si omis)
    Default = "value",         // Valeur par défaut YAML
    Enum = new[] { "a", "b" }, // Valeurs autorisées
    Example = "example",       // Exemple pour la documentation
    ItemsType = "string",      // Type des éléments si array
    ItemsFormat = "...",       // Format des éléments si array
    TypeDefinitionRef = "..."  // Référence à un type défini
)]
```

### Attribut `[ReturnSchema]` (sur TResponse)

```csharp
[ReturnSchema(
    Description = "...",       // Description du champ retourné
    Type = "boolean",          // Type JSON Schema (inféré si omis)
    Format = "...",            // Indice de format JSON Schema
    ItemsType = "...",         // Type des éléments si array
    ItemsFormat = "...",       // Format des éléments si array
    TypeDefinitionRef = "...", // Référence vers un type défini
    Example = true             // Exemple
)]
```

### Exemple concret — Request et Response

```csharp
using Orkeon.Domain.Attributes;

public sealed record TranslateRequest
{
    [FieldSchema(Description = "Text to translate", IsRequired = true,
                 Example = "Hello, world!")]
    public string Text { get; init; } = "";

    [FieldSchema(Description = "Target language ISO code", IsRequired = true,
                 Example = "fr", Enum = new[] { "fr", "de", "es", "it", "pt", "ja", "zh" })]
    public string TargetLanguage { get; init; } = "";

    [FieldSchema(Description = "Source language (auto-detected if omitted)",
                 IsRequired = false, Default = "auto")]
    public string SourceLanguage { get; init; } = "auto";
}

public sealed record TranslateResponse
{
    [ReturnSchema(Description = "Whether the translation succeeded")]
    public bool Success { get; init; }

    [ReturnSchema(Description = "Translated text")]
    public string TranslatedText { get; init; } = "";

    [ReturnSchema(Description = "Detected source language")]
    public string DetectedLanguage { get; init; } = "";

    [ReturnSchema(Description = "Error message if translation failed")]
    public string? Error { get; init; }
}
```

## Étape 3 — Implémenter la classe outil

Hériter de la classe de base choisie, poser un **attribut `[ToolContract]`** sur la
classe et implémenter `ExecuteTypedAsync`. Le premier argument positionnel du contrat
est le **nom visible par les agents** (`UniqueName` — la chaîne exacte que les listes
YAML `tools:` utilisent, celle que la porte CI des doc-claims vérifie contre
`docs/tools/inventory.md`) ; `Name`, `Description` et `Category` voyagent sur le même
attribut (`ToolBase` les lit : `Name => contract?.UniqueName ?? contract?.Name ??
GetType().Name`). Un outil sans contrat se rabat sur une surcharge des propriétés
`Name`/`Description` — chaque outil livré utilise l'attribut.

```csharp
[ToolContract("weather_lookup",
    Description = "Météo courante d'une ville via l'API du fournisseur.")]
public class WeatherTool : HttpToolBase<WeatherRequest, WeatherResponse> { … }
```

### Pipeline automatique

Le pipeline de `ToolBase<TRequest, TResponse>` est scellé (`sealed override ExecuteCoreAsync`) et effectue automatiquement :

1. **Injection des défauts YAML** : les paramètres optionnels absents sont complétés par leurs valeurs `Default`
2. **Désérialisation** : `Dictionary<string, object?>` → `TRequest` via `ComponentBase<TRequest, TResponse>`
3. **Validation** : appel optionnel de `ValidateTypedRequest(TRequest)` — retourner `null` si valide, un message d'erreur sinon
4. **Exécution** : appel de `ExecuteTypedAsync(TRequest, CancellationToken)` — votre logique métier
5. **Sérialisation** : `TResponse` → `Dictionary<string, object?>`
6. **Filtrage** : seuls les champs déclarés dans `[ReturnSchema]` sont inclus dans la réponse

### Exemple complet — Outil simple (sans dépendance externe)

```csharp
using Microsoft.Extensions.Logging;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Data;

public sealed class TranslateTool : ToolBase<TranslateRequest, TranslateResponse>
{
    public TranslateTool(ILogger<TranslateTool>? logger = null) : base(logger) { }

    public override string Name => "translate";

    public override string Description =>
        "Translate text from one language to another. " +
        "Source language is auto-detected if not specified.";

    protected override string? ValidateTypedRequest(TranslateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return "Text to translate cannot be empty.";

        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            return "Target language must be specified.";

        return null; // Valide
    }

    protected override async Task<TranslateResponse> ExecuteTypedAsync(
        TranslateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Logique métier — ici une traduction simplifiée
            var translated = $"[{request.TargetLanguage}] {request.Text}";

            return new TranslateResponse
            {
                Success = true,
                TranslatedText = translated,
                DetectedLanguage = request.SourceLanguage == "auto" ? "en" : request.SourceLanguage
            };
        }
        catch (Exception ex)
        {
            return new TranslateResponse
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}
```

## Étape 4 — Exemple complet — Outil avec dépendance externe (HTTP)

Pour un outil appelant une API externe, utiliser `HttpToolBase<TRequest, TResponse>` qui fournit un `HttpClient` partagé et la validation d'URL.

```csharp
using Microsoft.Extensions.Logging;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Domain.Attributes;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkeon.Tools.Web;

// --- Request ---
public sealed record WeatherRequest
{
    [FieldSchema(Description = "City name to get weather for",
                 IsRequired = true, Example = "Paris")]
    public string City { get; init; } = "";

    [FieldSchema(Description = "Temperature unit",
                 IsRequired = false, Default = "celsius",
                 Enum = new[] { "celsius", "fahrenheit" })]
    public string Unit { get; init; } = "celsius";
}

// --- Response ---
public sealed record WeatherResponse
{
    [ReturnSchema(Description = "Whether the request succeeded")]
    public bool Success { get; init; }

    [ReturnSchema(Description = "Current temperature")]
    public double Temperature { get; init; }

    [ReturnSchema(Description = "Weather description")]
    public string Description { get; init; } = "";

    [ReturnSchema(Description = "Error message if request failed")]
    public string? Error { get; init; }
}

// --- Tool ---
public sealed class WeatherTool : HttpToolBase<WeatherRequest, WeatherResponse>
{
    private const string BaseUrl = "https://api.weatherapi.com/v1";
    private readonly string _apiKey;

    // Ctor simple (HttpClient statique partagé). Pour la protection SSRF, utiliser le ctor
    // base(IUrlValidator, HttpHeaderSanitizer, HttpClient?, ILogger?).
    public WeatherTool(
        string apiKey,
        HttpClient? httpClient = null,
        ILogger<WeatherTool>? logger = null)
        : base(httpClient, logger)
    {
        _apiKey = apiKey;
    }

    public override string Name => "weather";

    public override string Description =>
        "Get current weather for a city. Returns temperature and conditions.";

    protected override async Task<WeatherResponse> ExecuteTypedAsync(
        WeatherRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{BaseUrl}/current.json?key={_apiKey}&q={Uri.EscapeDataString(request.City)}";

            // HttpClient est disponible via le champ protégé _httpClient (hérité de HttpToolBase)
            var response = await _httpClient.GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var tempC = json.GetProperty("current").GetProperty("temp_c").GetDouble();
            var condition = json.GetProperty("current")
                              .GetProperty("condition")
                              .GetProperty("text")
                              .GetString() ?? "Unknown";

            var temp = request.Unit == "fahrenheit" ? tempC * 9.0 / 5.0 + 32 : tempC;

            return new WeatherResponse
            {
                Success = true,
                Temperature = Math.Round(temp, 1),
                Description = condition
            };
        }
        catch (HttpRequestException ex)
        {
            return new WeatherResponse
            {
                Success = false,
                Error = $"HTTP error: {ex.Message}"
            };
        }
    }
}
```

## Étape 5 — Enregistrement et utilisation

### Option A — Injection directe via le builder

L'approche la plus simple : instancier l'outil et le passer au builder d'agent.

```csharp
var weatherTool = new WeatherTool(apiKey: "my-api-key");

var agent = new AgentBuilder()
    .Role("Weather Reporter")
    .Goal("Provide accurate weather forecasts")
    .WithTool(weatherTool)
    .Build();
```

### Option B — Enregistrement DI via `IServiceCollection`

Pour une intégration complète dans le pipeline d'injection de dépendances :

```csharp
// Dans une extension method ou Startup
services.AddSingleton<IBaseTool>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetService<ILogger<WeatherTool>>();
    return new WeatherTool(
        apiKey: config["Weather:ApiKey"]!,
        logger: logger);
});
```

L'outil est alors disponible via `IEnumerable<IBaseTool>`. **La résolution par nom
depuis le YAML exige un registre adossé à la DI** : le `AddOrkeonInfrastructure()`
par défaut enregistre le stub *vide* `InMemoryToolRegistry`, qui ne voit jamais vos
enregistrements `IBaseTool` — le runner host substitue `ServiceProviderToolRegistry`
(`Orkeon.Hosting`), et un hôte qui embarque doit faire de même :

```csharp
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();
```

### Option C — Via IToolRegistry

`IToolRegistry` (`Orkeon.Domain.Tools`, implémentation `InMemoryToolRegistry`) permet l'enregistrement et la résolution dynamique d'outils par nom :

```csharp
var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
var tool = await toolRegistry.GetToolByNameAsync("weather");
```

## Étape 6 — Pattern de composition interne (FileToolBase / HttpToolBase)

Les classes `FileToolBase<TRequest, TResponse>` et `HttpToolBase<TRequest, TResponse>` utilisent un pattern de composition via une classe interne privée `ComponentPipeline` qui hérite de `ComponentBase<TRequest, TResponse>` (couche Domain).

Ce pattern permet à la classe outil d'accéder aux méthodes de sérialisation/désérialisation du pipeline typé sans hériter directement de `ComponentBase` (qui est dans la couche Domain et ne connaît pas les concepts d'outils).

### Architecture du pipeline

```
ToolBase<TRequest, TResponse> (ou FileToolBase<> / HttpToolBase<>)
    │
    ├── Contient : private ComponentPipeline _pipeline
    │                  └── hérite de ComponentBase<TRequest, TResponse>
    │                       └── délègue à IComponentSerializer (statique)
    │                            └── JsonComponentSerializer (singleton)
    │
    └── sealed override ExecuteCoreAsync()
         1. MergeWithYamlDefaults(parameters)     → Dict enrichi
         2. _pipeline.Deserialize(merged)          → TRequest
         3. ValidateTypedRequest(typedRequest)     → null | erreur
         4. ExecuteTypedAsync(request, ct)         → TResponse  ← VOTRE CODE
         5. _pipeline.Serialize(typedResponse)     → Dict
         6. FilterOutput(resultDict)               → Dict filtré
         7. return ToolCallResponse(success, result, error)
```

### Extrait du code source (`FileToolBaseGeneric.cs`)

```csharp
public abstract partial class FileToolBase<TRequest, TResponse> : FileToolBase
    where TRequest : class, new()
    where TResponse : class
{
    private readonly ComponentPipeline _pipeline = new();

    // Le pipeline scellé empêche les sous-classes de court-circuiter
    // la sérialisation ou la validation
    protected sealed override async Task<ProtocolToolCallResponse> ExecuteCoreAsync(
        ProtocolToolCallRequest request, CancellationToken cancellationToken)
    {
        var mergedParams = MergeWithYamlDefaults(request.Parameters);
        var typedRequest = _pipeline.Deserialize(mergedParams);

        var validationError = ValidateTypedRequest(typedRequest);
        if (validationError is not null)
            return new ProtocolToolCallResponse(Success: false, Result: null, Error: validationError);

        var typedResponse = await ExecuteTypedAsync(typedRequest, cancellationToken);
        var resultDict = _pipeline.Serialize(typedResponse);
        var filteredResult = FilterOutput(resultDict);
        // ... propagation success/error ...
        return new ProtocolToolCallResponse(Success: success, Result: filteredResult, Error: error);
    }

    // Votre point d'extension — logique métier pure
    protected abstract Task<TResponse> ExecuteTypedAsync(
        TRequest request, CancellationToken cancellationToken);

    // Classe interne de composition
    private sealed class ComponentPipeline : ComponentBase<TRequest, TResponse>
    {
        public TRequest Deserialize(Dictionary<string, object> parameters)
            => DeserializeRequest(parameters);
        public Dictionary<string, object> Serialize(TResponse response)
            => SerializeResponse(response);
        protected override Task<TResponse> ExecuteTypedAsync(
            TRequest request, CancellationToken ct)
            => throw new NotSupportedException("Pipeline helper does not execute.");
    }
}
```

### Sérialisation : snake_case et coercition de types

Le `JsonComponentSerializer` (`Orkeon.Infrastructure.Serialization`) utilise `JsonNamingPolicy.SnakeCaseLower` et inclut 10 convertisseurs tolérants pour gérer les valeurs YAML qui arrivent sous forme de strings — les six ci-dessous plus `TolerantEnumConverterFactory`, `UriTolerantConverter`, `ImmutableArrayEnumTolerantConverter<EdgeKind>` et `ImmutableArrayStringTolerantConverter` :

- `BoolTolerantConverter` : `"true"`, `"1"`, `"yes"` → `true`
- `IntTolerantConverter` : `"42"` → `42`
- `LongTolerantConverter` : `"123456789"` → `123456789L`
- `DecimalInvariantConverter` : `"3.14"` → `3.14m` (culture-invariant)
- `DoubleInvariantConverter` : `"3.14"` → `3.14d`
- `RawObjectConverter` : unwrap `JsonElement` vers types .NET natifs

Cela garantit que les paramètres YAML (qui sont tous des strings à la désérialisation) sont correctement convertis vers les types C# attendus par `TRequest`.

## Étape 7 — Enregistrer un outil dans une suite (package NuGet)

Si l'outil est destiné à être distribué dans un package, créer une extension DI dans le même projet :

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Tools.MyPackage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrkeonMyPackageTools(
        this IServiceCollection services)
    {
        // Un enregistrement IBaseTool par outil. PAS TryAddSingleton<IBaseTool, …>
        // deux fois : TryAdd est indexé sur le type de SERVICE, le second appel
        // serait un no-op silencieux. Les outils à arguments de ctor hors-DI
        // (l'apiKey de WeatherTool) exigent la forme factory.
        services.AddSingleton<IBaseTool>(sp => new WeatherTool(
            apiKey: sp.GetRequiredService<IConfiguration>()["Weather:ApiKey"]!,
            logger: sp.GetService<ILogger<WeatherTool>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, TranslateTool>());

        // Utilisables par nom en YAML dès qu'un IToolRegistry adossé à la DI est
        // enregistré (ServiceProviderToolRegistry — voir l'Option A plus haut).
        return services;
    }
}
```

Usage dans le `Program.cs` :

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();
services.AddOrkeonMyPackageTools();  // Vos outils custom
```

## Récapitulatif du pattern

```
1. Choisir la base class    →  ToolBase<> / FileToolBase<> / HttpToolBase<>
2. Définir TRequest          →  Record avec [FieldSchema] sur chaque propriété
3. Définir TResponse         →  Record avec [ReturnSchema] sur chaque propriété
4. Implémenter la classe     →  override Name, Description, ExecuteTypedAsync
5. (Optionnel) Validation    →  override ValidateTypedRequest
6. Enregistrer               →  Builder / DI / ToolFactory
```

Le schéma JSON est auto-généré par `ToolSchemaGenerator` à partir des attributs — aucune maintenance manuelle requise.
