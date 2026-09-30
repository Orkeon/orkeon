> 🇬🇧 [English version](../../tools/new-tool-pattern.md)

> **Voir aussi** : [Inventaire des outils](./inventory.md) · [Retour à l'index](../INDEX.md)

# Pattern — Implémenter un nouvel outil

Ce guide détaille le processus complet de création d'un outil Orkeon, basé sur les classes et interfaces existantes dans le code source.

## Étape 1 — Choisir la classe de base

Orkeon fournit trois classes de base dans `Orkeon.Tools.Abstractions.Base` :

| Classe de base | Usage | Protection intégrée |
|----------------|-------|-------------------|
| `ToolBase<TRequest, TResponse>` | Outil générique | Aucune spécialisation |
| `FileToolBase<TRequest, TResponse>` | Opérations sur fichiers | `IFileSystemService` (le VFS — premier argument de ctor obligatoire), un `IPathValidator` optionnel (défense en profondeur contre la traversée de chemin), les helpers `ResolveVirtualPath(path, FileAccessRights)` et `EnsureDirectoryExistsAsync(...)` |
| `HttpToolBase<TRequest, TResponse>` | Opérations HTTP/API | Un `HttpClient` partagé qui refuse les redirections (ou le vôtre, ou un client d'`IHttpClientFactory`), `ValidateUrlAsync(uri, ct)` — l'`IUrlValidator` fourni, sinon une garde SSRF par défaut fail-closed — et `SanitizeHeaders(...)` quand un `HttpHeaderSanitizer` est fourni |

La classe de base `ToolBase<TRequest, TResponse>` hérite de `ToolBase` (non générique), qui
implémente `IBaseTool` (`Orkeon.Domain.Tools`), l'unique contrat que partagent tous les
outils : tout `IBaseTool` enregistré atteint l'agent qui le nomme. Les deux bases
spécialisées dérivent aussi de `ToolBase`.

Un outil de fichiers ne touche jamais lui-même `System.IO.File`/`Directory` : il résout
chaque chemin via le VFS (voir [Conformité VFS](../architecture/vfs-compliance.md)) —
l'analyseur `Orkeon.Compliance.Vfs` rejette les appels directs dans le code du framework.

## Étape 2 — Définir les types Request et Response

Chaque outil typé nécessite un type `TRequest` (paramètres d'entrée — une classe avec un
constructeur sans paramètre ; un `sealed record` convient) et un type `TResponse`
(résultat). Les attributs `[FieldSchema]` et `[ReturnSchema]` (`Orkeon.Domain.Attributes`)
servent à générer automatiquement le schéma JSON exposé au LLM (`ToolSchemaGenerator`,
`Orkeon.Domain.Tools`).

- **Seule une propriété portant `[FieldSchema]` est un paramètre.** Une propriété sans
  l'attribut est invisible pour le LLM (elle garde sa valeur C# par défaut).
- **Les noms de paramètres sont les noms de propriétés en snake_case** (`TargetLanguage` →
  `target_language`), dans le schéma comme dans le désérialiseur. Un `[JsonPropertyName]`
  doit donc épeler le même nom snake_case, sinon le schéma et la liaison divergent.
- **Les retours sont opt-in par attribut** : dès qu'une propriété de `TResponse` porte
  `[ReturnSchema]`, seules les propriétés annotées reviennent à l'agent ; quand aucune ne le
  porte, toutes reviennent.

### Attribut `[FieldSchema]` (sur TRequest)

Propriétés disponibles :

```csharp
[FieldSchema(
    Description = "...",       // Description lisible pour le LLM
    Type = "string",           // Type JSON Schema (inféré si omis)
    Format = "uri",            // Format OpenAPI (inféré si omis)
    IsRequired = true,         // Requis ; si omis, inféré de la nullabilité C#
                               // (un string ou un int non nullable EST requis —
                               // IsRequired = false pour un paramètre optionnel à défaut)
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
`docs/tools/inventory.md` ; tenez-vous à `[a-zA-Z0-9_-]`, les fournisseurs compatibles
OpenAI rejettent tout autre caractère) ; `Name` (un nom d'affichage), `Description` et
`Category` voyagent sur le même attribut (`ToolBase` les lit : `Name =>
contract?.UniqueName ?? contract?.Name ?? GetType().Name`). Surcharger les propriétés
`Name`/`Description` fonctionne aussi — les outils RaggableTree, de session et RAG le font —
et la porte CI lit l'une ou l'autre forme.

```csharp
[ToolContract("weather_lookup",
    Description = "Météo courante d'une ville via l'API du fournisseur.")]
public class WeatherTool : HttpToolBase<WeatherRequest, WeatherResponse> { … }
```

Trois autres propriétés virtuelles méritent d'être déclarées :

| Propriété | Défaut | Qui la lit |
|---|---|---|
| `Access` | `ToolAccess.Unspecified` | La permission gate par appel (`Read` / `Edit` / `Execute`) ; non spécifié est classé en fail-closed |
| `Category` | la `Category` du contrat, sinon `"General"` (`"File Operations"` / `"Web Operations"` pour les deux bases spécialisées) | Catalogues et listes |
| `RequiresHumanApproval` | `false` | Les appelants qui gèrent une approbation |

### Pipeline automatique

Avant le pipeline typé, `ToolBase.CallAsync` vérifie les paramètres de l'appel contre le
schéma généré (paramètres requis, types, valeurs autorisées) et répond un `ToolCallResponse`
en échec sans appeler votre code. Le pipeline de `ToolBase<TRequest, TResponse>` lui-même
est scellé (`sealed override ExecuteCoreAsync`) et effectue automatiquement :

1. **Injection des défauts YAML** : les paramètres optionnels absents sont complétés par leurs valeurs `Default`
2. **Désérialisation** : `Dictionary<string, object?>` → `TRequest` via `ComponentBase<TRequest, TResponse>` — une valeur inconvertible répond `Invalid parameters: …`
3. **Validation** : appel optionnel de `ValidateTypedRequest(TRequest)` — retourner `null` si valide, un message d'erreur sinon
4. **Exécution** : appel de `ExecuteTypedAsync(TRequest, CancellationToken)` — votre logique métier
5. **Sérialisation** : `TResponse` → `Dictionary<string, object?>`
6. **Filtrage** : seuls les retours déclarés sont inclus dans la réponse (voir l'étape 2)
7. **Issue** : un booléen `success` dans la réponse décide du succès de l'appel ; quand il
   vaut `false`, la chaîne `error` (ou la liste `errors`) devient l'erreur que voit l'agent.
   Une réponse sans `success` compte comme un succès.

Une exception levée par `ExecuteTypedAsync` n'est pas fatale non plus : `CallAsync` la
transforme en réponse en échec (`Tool execution failed: …`), et une annulation en
`Operation cancelled`.

### Exemple complet — Outil simple (sans dépendance externe)

```csharp
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace MyCompany.Tools;

[ToolContract("translate",
    Description = "Translate text from one language to another. " +
                  "Source language is auto-detected if not specified.")]
public sealed class TranslateTool : ToolBase<TranslateRequest, TranslateResponse>
{
    public TranslateTool(ILogger<TranslateTool>? logger = null) : base(logger) { }

    public override ToolAccess Access => ToolAccess.Read;

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

Pour un outil appelant une API externe, utiliser `HttpToolBase<TRequest, TResponse>` qui fournit un `HttpClient` partagé et la validation d'URL — que votre code appelle (`ValidateUrlAsync`) : rien ne valide une URL dans votre dos.

```csharp
using Microsoft.Extensions.Logging;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyCompany.Tools;

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
[ToolContract("weather",
    Description = "Get current weather for a city. Returns temperature and conditions.")]
public sealed class WeatherTool : HttpToolBase<WeatherRequest, WeatherResponse>
{
    private const string BaseUrl = "https://api.weatherapi.com/v1";
    private readonly string _apiKey;

    // Ctor simple (HttpClient statique partagé, redirections refusées). Pour brancher
    // l'IUrlValidator et le HttpHeaderSanitizer de l'hôte, utiliser le ctor
    // base(IUrlValidator, HttpHeaderSanitizer, HttpClient?, ILogger?) ; sans eux,
    // ValidateUrlAsync applique la garde par défaut fail-closed.
    public WeatherTool(
        string apiKey,
        HttpClient? httpClient = null,
        ILogger<WeatherTool>? logger = null)
        : base(httpClient, logger)
    {
        _apiKey = apiKey;
    }

    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<WeatherResponse> ExecuteTypedAsync(
        WeatherRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = new Uri($"{BaseUrl}/current.json?key={_apiKey}&q={Uri.EscapeDataString(request.City)}");

            // Contrôle SSRF — obligatoire dès qu'une partie de l'URL vient de l'agent
            var check = await ValidateUrlAsync(url, cancellationToken).ConfigureAwait(false);
            if (!check.IsAllowed)
                return new WeatherResponse { Success = false, Error = check.DenialReason };

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
// Dans une méthode d'extension ou au démarrage
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

`IToolRegistry` (`Orkeon.Domain.Tools` ; `ServiceProviderToolRegistry` dans les runners, le stub `InMemoryToolRegistry` par défaut) permet l'enregistrement dynamique (`RegisterToolAsync`) et la résolution d'outils par nom :

```csharp
var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
var tool = await toolRegistry.GetToolByNameAsync("weather");
```

## Étape 6 — Pattern de composition interne (FileToolBase / HttpToolBase)

`ToolBase<TRequest, TResponse>`, `FileToolBase<TRequest, TResponse>` et `HttpToolBase<TRequest, TResponse>` utilisent chacune un pattern de composition via une classe interne privée `ComponentPipeline` qui hérite de `ComponentBase<TRequest, TResponse>` (couche Domain) — les bases spécialisées dérivent des `FileToolBase`/`HttpToolBase` non génériques, pas de `ToolBase<,>`, et portent donc chacune leur propre copie du pipeline scellé.

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
    // la sérialisation ou la validation (simplifié : la vraie méthode intercepte
    // aussi JsonException → "Invalid parameters: …")
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
        // ... propagation success/erreur depuis les champs `success` / `error` / `errors` ...
        return new ProtocolToolCallResponse(Success: success, Result: filteredResult, Error: error);
    }

    // Votre point d'extension — logique métier pure
    protected abstract Task<TResponse> ExecuteTypedAsync(
        TRequest request, CancellationToken cancellationToken);

    // Classe interne de composition
    private sealed class ComponentPipeline : ComponentBase<TRequest, TResponse>
    {
        public TRequest Deserialize(Dictionary<string, object?> parameters)
            => DeserializeRequest(parameters);
        public Dictionary<string, object?> Serialize(TResponse response)
            => SerializeResponse(response);
        protected override Task<TResponse> ExecuteTypedAsync(
            TRequest request, CancellationToken ct)
            => throw new NotSupportedException("Pipeline helper does not execute.");
    }
}
```

### Sérialisation : snake_case et coercition de types

Le `JsonComponentSerializer` (`Orkeon.Infrastructure.Serialization`) utilise `JsonNamingPolicy.SnakeCaseLower` et inclut 10 convertisseurs tolérants pour gérer les valeurs YAML qui arrivent sous forme de strings — les six ci-dessous plus `TolerantEnumConverterFactory` (noms d'enum reconnus sans tenir compte de la casse, un nom inconnu répondu avec la liste des noms valides), `UriTolerantConverter`, `ImmutableArrayEnumTolerantConverter<EdgeKind>` et `ImmutableArrayStringTolerantConverter` :

- `BoolTolerantConverter` : `"true"`/`"false"` (quelle que soit la casse), `"1"`/`"0"` et les nombres (non nul → `true`) ; `"yes"` n'est **pas** accepté
- `IntTolerantConverter` : `"42"` → `42`
- `LongTolerantConverter` : `"123456789"` → `123456789L`
- `DecimalInvariantConverter` : `"3.14"` → `3.14m` (culture-invariant)
- `DoubleInvariantConverter` : `"3.14"` → `3.14d`
- `RawObjectConverter` : unwrap `JsonElement` vers types .NET natifs

Cela garantit que les paramètres YAML (qui sont tous des strings à la désérialisation) sont correctement convertis vers les types C# attendus par `TRequest`.

Le sérialiseur vaut pour tout le processus : `ComponentBase.DefaultSerializer`.
`AddOrkeonInfrastructure()` le pose (`TrySetDefaultSerializer`, qui n'écrase jamais celui
qu'un hôte a déjà posé) ; un processus qui construit des outils sans lui — un projet de tests
unitaires, typiquement — le pose lui-même une fois (voir l'étape 8), sinon le premier appel
lève `ComponentBase.DefaultSerializer has not been configured`.

### Un outil de fichiers, à la manière du VFS

`FileReadTool.cs` (`Orkeon.Tools.FileSystem`) est la référence : la requête porte un chemin
**virtuel**, l'outil le résout avec le droit dont il a besoin, et chaque octet passe par
`_fileSystemService` :

```csharp
[ToolContract("line_count", Description = "Count the lines of a text file.")]
public sealed class LineCountTool : FileToolBase<LineCountRequest, LineCountResponse>
{
    // FileReadTool prend aussi un IPathValidator (base(fs, pathValidator, logger)) pour
    // une défense en profondeur sur le chemin physique résolu
    public LineCountTool(IFileSystemService fileSystemService, ILogger<LineCountTool>? logger = null)
        : base(fileSystemService, logger) { }

    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<LineCountResponse> ExecuteTypedAsync(
        LineCountRequest request, CancellationToken cancellationToken)
    {
        // Contrôle montage + droits (FileAccessRights.Read) ; les chemins physiques ne fuient jamais
        var check = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        if (!check.IsAllowed)
            return new LineCountResponse { Success = false, Error = check.DenialReason };

        var stream = await _fileSystemService.OpenReadStreamAsync(request.Path, cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var lines = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            lines++;
        return new LineCountResponse { Success = true, Lines = lines };
    }
}
```

Un outil qui écrit demande `FileAccessRights.Write` (ou `Create`), et appelle
`EnsureDirectoryExistsAsync(virtualPath, ct)` avant de créer un fichier dans un nouveau dossier.

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
        // enregistré (ServiceProviderToolRegistry — voir l'Option B plus haut).
        return services;
    }
}
```

Usage dans le `Program.cs` :

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();
services.AddOrkeonMyPackageTools();  // Vos outils custom
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>(); // résolution par nom pour le YAML
```

## Étape 8 — Tester l'outil

Les tests d'outils suivent les conventions du dépôt : **xUnit avec ses seules assertions
natives**, aucun framework de mock ni bibliothèque d'assertions fluentes. Les doubles sont des
classes écrites à la main, nommées `Mock*`, `Fake*` ou `Stub*`, qui implémentent l'interface de
production, rangées dans un dossier `Doubles/` du projet de test (référence :
`tests/core/Orkeon.Infrastructure.Tests/Doubles/MockTaskRepository.cs`).

- **Sérialiseur.** Chaque projet de tests d'outils pose le sérialiseur de composants une fois,
  dans un initialiseur de module (`TestModuleInitializer.cs`, comme dans
  `tests/tools/Orkeon.Tools.FileSystem.Tests/`) :

  ```csharp
  internal static class TestModuleInitializer
  {
      [ModuleInitializer]
      internal static void Initialize()
      {
          if (!ComponentBase.IsDefaultSerializerConfigured)
              ComponentBase.DefaultSerializer = JsonComponentSerializer.Instance;
      }
  }
  ```

- **Système de fichiers.** `Orkeon.Tests.Shared` (`tests/shared/`) fournit des doubles du VFS :
  `FakeFileSystemService` (en mémoire), `DiskBackedFileSystemService` et
  `PassThroughFileSystemService` (vrai disque, pour les tests seulement — voir l'exception VFS
  des tests), `ThrowingFileSystemService`.
- **Appelez l'outil comme le fait un agent** — `CallAsync` avec un `ToolCallRequest` dont les
  paramètres portent les noms snake_case — pour exercer le contrôle de schéma, les défauts, la
  désérialisation et le filtrage de sortie :

  ```csharp
  [Fact]
  public async Task Should_CountLines_When_FileExists()
  {
      var fileSystem = new FakeFileSystemService()
          .AddMount("/workspace")
          .AddFile("/workspace/notes.txt", "one\ntwo\nthree");
      var tool = new LineCountTool(fileSystem);

      var response = await tool.CallAsync(new ToolCallRequest(
          ToolName: "line_count",
          Parameters: new Dictionary<string, object?> { ["path"] = "/workspace/notes.txt" }));

      Assert.True(response.Success, response.Error);
      var result = Assert.IsType<Dictionary<string, object?>>(response.Result);
      Assert.Equal(3, Convert.ToInt32(result["lines"], CultureInfo.InvariantCulture));
  }
  ```

Un nouvel outil intégré doit aussi figurer dans [l'inventaire](./inventory.md) : la porte
doc-claims (`scripts/check-doc-claims.py`) échoue quand un nom d'outil du code y manque, et le
compte d'outils des pages d'accueil bouge avec lui.

## Récapitulatif du pattern

```
1. Choisir la classe de base →  ToolBase<> / FileToolBase<> / HttpToolBase<>
2. Définir TRequest          →  Record avec [FieldSchema] sur chaque paramètre
3. Définir TResponse         →  Record avec [ReturnSchema] sur chaque champ retourné
4. Implémenter la classe     →  [ToolContract("nom", Description = …)], ExecuteTypedAsync, Access
5. (Optionnel) Validation    →  override ValidateTypedRequest
6. Enregistrer               →  Builder / DI (+ ServiceProviderToolRegistry) / IToolRegistry
7. Tester                    →  xUnit, doubles écrits à la main, CallAsync avec paramètres snake_case
```

Le schéma JSON est auto-généré par `ToolSchemaGenerator` à partir des attributs — aucune maintenance manuelle requise.
