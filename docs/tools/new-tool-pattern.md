> 🇫🇷 [Version française](../fr/tools/new-tool-pattern.md)

> **See also**: [Tool inventory](./inventory.md) · [Back to index](../INDEX.md)

# Pattern — Implementing a new tool

This guide details the complete process of creating an Orkeon tool, based on the classes and interfaces existing in the source code.

## Step 1 — Choose the base class

Orkeon provides three base classes in `Orkeon.Tools.Abstractions.Base`:

| Base class | Usage | Built-in protection |
|----------------|-------|-------------------|
| `ToolBase<TRequest, TResponse>` | Generic tool | No specialization |
| `FileToolBase<TRequest, TResponse>` | File operations | `IPathValidator` (path traversal protection) |
| `HttpToolBase<TRequest, TResponse>` | HTTP/API operations | `IUrlValidator` (SSRF protection), `HttpHeaderSanitizer` |

The `ToolBase<TRequest, TResponse>` base class inherits from `ToolBase` (non-generic), which implements `IBaseTool` and `ITool` (`Orkeon.Domain.Tools`).

## Step 2 — Define the Request and Response types

Each typed tool requires a `TRequest` record (input parameters) and a `TResponse` record (result). The `[FieldSchema]` and `[ReturnSchema]` attributes (`Orkeon.Domain.Attributes`) are used to automatically generate the JSON schema exposed to the LLM.

### `[FieldSchema]` attribute (on TRequest)

Available properties:

```csharp
[FieldSchema(
    Description = "...",       // Human-readable description for the LLM
    Type = "string",           // JSON Schema type (inferred when omitted)
    Format = "uri",            // OpenAPI format (inferred when omitted)
    IsRequired = true,         // Required (inferred from C# nullability when omitted)
    Default = "value",         // YAML default value
    Enum = new[] { "a", "b" }, // Allowed values
    Example = "example",       // Example for the documentation
    ItemsType = "string",      // Element type when array
    TypeDefinitionRef = "..."  // Reference to a defined type
)]
```

### `[ReturnSchema]` attribute (on TResponse)

```csharp
[ReturnSchema(
    Description = "...",       // Description of the returned field
    Type = "boolean",          // JSON Schema type (inferred when omitted)
    Example = true             // Exemple
)]
```

### Concrete example — Request and Response

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

## Step 3 — Implement the tool class

Inherit from the chosen base class, define the `Name` and `Description` properties, and implement `ExecuteTypedAsync`.

### Automatic pipeline

The `ToolBase<TRequest, TResponse>` pipeline is sealed (`sealed override ExecuteCoreAsync`) and automatically performs:

1. **YAML defaults injection**: missing optional parameters are filled in with their `Default` values
2. **Deserialization**: `Dictionary<string, object>` → `TRequest` via `ComponentBase<TRequest, TResponse>`
3. **Validation**: optional call to `ValidateTypedRequest(TRequest)` — return `null` if valid, an error message otherwise
4. **Execution**: call to `ExecuteTypedAsync(TRequest, CancellationToken)` — your business logic
5. **Serialization**: `TResponse` → `Dictionary<string, object>`
6. **Filtering**: only the fields declared in `[ReturnSchema]` are included in the response

### Complete example — Simple tool (no external dependency)

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
            // Business logic — a simplified translation here
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

## Step 4 — Complete example — Tool with an external dependency (HTTP)

For a tool calling an external API, use `HttpToolBase<TRequest, TResponse>`, which provides a shared `HttpClient` and URL validation.

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

    // Simple ctor (shared static HttpClient). For SSRF protection, use the
    // base(IUrlValidator, HttpHeaderSanitizer, HttpClient?, ILogger?) ctor.
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

            // HttpClient is available through the protected _httpClient field (inherited from HttpToolBase)
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

## Step 5 — Registration and usage

### Option A — Direct injection via the builder

The simplest approach: instantiate the tool and pass it to the agent builder.

```csharp
var weatherTool = new WeatherTool(apiKey: "my-api-key");

var agent = new AgentBuilder()
    .Role("Weather Reporter")
    .Goal("Provide accurate weather forecasts")
    .WithTool(weatherTool)
    .Build();
```

### Option B — DI registration via `IServiceCollection`

For full integration into the dependency injection pipeline:

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

The tool will then be available via `IEnumerable<IBaseTool>` or resolved by the `IToolRegistry` (`Orkeon.Domain.Tools`).

### Option C — Via IToolRegistry

`IToolRegistry` (`Orkeon.Domain.Tools`, implementation `InMemoryToolRegistry`) enables dynamic registration and resolution of tools by name:

```csharp
var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
var tool = await toolRegistry.GetToolByNameAsync("weather");
```

## Step 6 — Internal composition pattern (FileToolBase / HttpToolBase)

The `FileToolBase<TRequest, TResponse>` and `HttpToolBase<TRequest, TResponse>` classes use a composition pattern via a private inner class `ComponentPipeline` that inherits from `ComponentBase<TRequest, TResponse>` (Domain layer).

This pattern lets the tool class access the typed pipeline's serialization/deserialization methods without inheriting directly from `ComponentBase` (which lives in the Domain layer and knows nothing about tool concepts).

### Pipeline architecture

```
ToolBase<TRequest, TResponse> (ou FileToolBase<> / HttpToolBase<>)
    │
    ├── Contains: private ComponentPipeline _pipeline
    │                  └── inherits ComponentBase<TRequest, TResponse>
    │                       └── delegates to IComponentSerializer (static)
    │                            └── JsonComponentSerializer (singleton)
    │
    └── sealed override ExecuteCoreAsync()
         1. MergeWithYamlDefaults(parameters)     → enriched Dict
         2. _pipeline.Deserialize(merged)          → TRequest
         3. ValidateTypedRequest(typedRequest)     → null | error
         4. ExecuteTypedAsync(request, ct)         → TResponse  ← YOUR CODE
         5. _pipeline.Serialize(typedResponse)     → Dict
         6. FilterOutput(resultDict)               → filtered Dict
         7. return ToolCallResponse(success, result, error)
```

### Source code excerpt (`FileToolBaseGeneric.cs`)

```csharp
public abstract partial class FileToolBase<TRequest, TResponse> : FileToolBase
    where TRequest : class, new()
    where TResponse : class
{
    private readonly ComponentPipeline _pipeline = new();

    // The sealed pipeline prevents subclasses from bypassing
    // serialization or validation
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

    // Your extension point — pure business logic
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

### Serialization: snake_case and type coercion

The `JsonComponentSerializer` (`Orkeon.Infrastructure.Serialization`) uses `JsonNamingPolicy.SnakeCaseLower` and includes 6 tolerant converters to handle YAML values arriving as strings:

- `BoolTolerantConverter`: `"true"`, `"1"`, `"yes"` → `true`
- `IntTolerantConverter`: `"42"` → `42`
- `LongTolerantConverter`: `"123456789"` → `123456789L`
- `DecimalInvariantConverter`: `"3.14"` → `3.14m` (culture-invariant)
- `DoubleInvariantConverter`: `"3.14"` → `3.14d`
- `RawObjectConverter`: unwraps `JsonElement` into native .NET types

This guarantees that YAML parameters (which are all strings at deserialization time) are correctly converted to the C# types expected by `TRequest`.

## Step 7 — Registering a tool in a suite (NuGet package)

If the tool is meant to be distributed in a package, create a DI extension in the same project:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkeon.Tools.MyPackage;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrkeonMyPackageTools(
        this IServiceCollection services)
    {
        // Register each tool as an IBaseTool
        services.TryAddSingleton<IBaseTool, WeatherTool>();
        services.TryAddSingleton<IBaseTool, TranslateTool>();

        // The tools become automatically available in IToolRegistry
        // and usable by name in YAML files
        return services;
    }
}
```

Usage in `Program.cs`:

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();
services.AddOrkeonMyPackageTools();  // Vos outils custom
```

## Pattern recap

```
1. Pick the base class       →  ToolBase<> / FileToolBase<> / HttpToolBase<>
2. Define TRequest           →  Record with [FieldSchema] on each property
3. Define TResponse          →  Record with [ReturnSchema] on each property
4. Implement the class       →  override Name, Description, ExecuteTypedAsync
5. (Optional) Validation     →  override ValidateTypedRequest
6. Enregistrer               →  Builder / DI / ToolFactory
```

The JSON schema is auto-generated by `ToolSchemaGenerator` from the attributes — no manual maintenance required.
