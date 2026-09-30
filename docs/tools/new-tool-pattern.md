> 🇫🇷 [Version française](../fr/tools/new-tool-pattern.md)

> **See also**: [Tool inventory](./inventory.md) · [Back to index](../INDEX.md)

# Pattern — Implementing a new tool

This guide details the complete process of creating an Orkeon tool, based on the classes and interfaces existing in the source code.

## Step 1 — Choose the base class

Orkeon provides three base classes in `Orkeon.Tools.Abstractions.Base`:

| Base class | Usage | Built-in protection |
|----------------|-------|-------------------|
| `ToolBase<TRequest, TResponse>` | Generic tool | No specialization |
| `FileToolBase<TRequest, TResponse>` | File operations | `IFileSystemService` (the VFS — mandatory first ctor argument), an optional `IPathValidator` (defense in depth against traversal), `ResolveVirtualPath(path, FileAccessRights)` and `EnsureDirectoryExistsAsync(...)` helpers |
| `HttpToolBase<TRequest, TResponse>` | HTTP/API operations | A shared, redirect-free `HttpClient` (or yours, or one from `IHttpClientFactory`), `ValidateUrlAsync(uri, ct)` — the given `IUrlValidator`, else a fail-closed default SSRF guard — and `SanitizeHeaders(...)` when an `HttpHeaderSanitizer` is given |

The `ToolBase<TRequest, TResponse>` base class inherits from `ToolBase` (non-generic), which
implements `ITool` (`Orkeon.Domain.Common`, itself an `IBaseTool` from `Orkeon.Domain.Tools`).
That matters: `CrewFactory` attaches only an `ITool` to an agent, so a class implementing
`IBaseTool` alone is registered but never reaches a YAML agent. The two specialized bases
derive from `ToolBase` too.

A file tool must never touch `System.IO.File`/`Directory` itself: it resolves every path
through the VFS (see [VFS compliance](../architecture/vfs-compliance.md)) — the
`Orkeon.Compliance.Vfs` analyzer rejects the direct calls in framework code.

## Step 2 — Define the Request and Response types

Each typed tool requires a `TRequest` type (input parameters — a class with a parameterless
constructor, a `sealed record` is fine) and a `TResponse` type (result). The `[FieldSchema]`
and `[ReturnSchema]` attributes (`Orkeon.Domain.Attributes`) are used to automatically
generate the JSON schema exposed to the LLM (`ToolSchemaGenerator`, `Orkeon.Domain.Tools`).

- **Only a property carrying `[FieldSchema]` is a parameter.** A property without it is
  invisible to the LLM (it keeps its C# default).
- **Parameter names are the property names in snake_case** (`TargetLanguage` →
  `target_language`), both in the schema and in the deserializer. A `[JsonPropertyName]`
  must therefore spell the same snake_case name, or the schema and the binding disagree.
- **Returns are opt-in by attribute:** when at least one property of `TResponse` carries
  `[ReturnSchema]`, only the annotated properties are returned to the agent; when none does,
  every property is.

### `[FieldSchema]` attribute (on TRequest)

Available properties:

```csharp
[FieldSchema(
    Description = "...",       // Human-readable description for the LLM
    Type = "string",           // JSON Schema type (inferred when omitted)
    Format = "uri",            // OpenAPI format (inferred when omitted)
    IsRequired = true,         // Required; when omitted, inferred from C# nullability
                               // (a non-nullable string or int IS required — say
                               // IsRequired = false for an optional one with a default)
    Default = "value",         // YAML default value
    Enum = new[] { "a", "b" }, // Allowed values
    Example = "example",       // Example for the documentation
    ItemsType = "string",      // Element type when array
    ItemsFormat = "...",       // Element format when array
    TypeDefinitionRef = "..."  // Reference to a defined type
)]
```

### `[ReturnSchema]` attribute (on TResponse)

```csharp
[ReturnSchema(
    Description = "...",       // Description of the returned field
    Type = "boolean",          // JSON Schema type (inferred when omitted)
    Format = "...",            // JSON Schema format hint
    ItemsType = "...",         // Element type when array
    ItemsFormat = "...",       // Element format when array
    TypeDefinitionRef = "...", // Reference to a defined type
    Example = true             // Example
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

Inherit from the chosen base class, put a **`[ToolContract]` attribute** on the class
and implement `ExecuteTypedAsync`. The contract's first positional argument is the
**agent-visible name** (`UniqueName` — the exact string YAML `tools:` lists use, and
the one the doc-claims CI gate checks against `docs/tools/inventory.md`; keep it to
`[a-zA-Z0-9_-]`, since OpenAI-compatible providers reject anything else); `Name` (a
display name), `Description` and `Category` ride on the same attribute (`ToolBase` reads
them: `Name => contract?.UniqueName ?? contract?.Name ?? GetType().Name`). Overriding the
`Name`/`Description` properties works as well — the RaggableTree, session and RAG tools do
it — and the CI gate reads either form.

```csharp
[ToolContract("weather_lookup",
    Description = "Current weather for a city via the provider API.")]
public class WeatherTool : HttpToolBase<WeatherRequest, WeatherResponse> { … }
```

Three more virtual properties are worth declaring:

| Property | Default | What reads it |
|---|---|---|
| `Access` | `ToolAccess.Unspecified` | The per-call permission gate (`Read` / `Edit` / `Execute`); unspecified is classified fail-closed |
| `Category` | the contract's `Category`, else `"General"` (`"File Operations"` / `"Web Operations"` for the two specialized bases) | Catalogues and listings |
| `RequiresHumanApproval` | `false` | Approval-aware callers |

### Automatic pipeline

Before the typed pipeline runs, `ToolBase.CallAsync` checks the call's parameters against the
generated schema (required parameters, types, allowed values) and answers a failed `ToolCallResponse` without
calling your code. The `ToolBase<TRequest, TResponse>` pipeline itself is sealed
(`sealed override ExecuteCoreAsync`) and automatically performs:

1. **YAML defaults injection**: missing optional parameters are filled in with their `Default` values
2. **Deserialization**: `Dictionary<string, object?>` → `TRequest` via `ComponentBase<TRequest, TResponse>` — a value that cannot be converted answers `Invalid parameters: …`
3. **Validation**: optional call to `ValidateTypedRequest(TRequest)` — return `null` if valid, an error message otherwise
4. **Execution**: call to `ExecuteTypedAsync(TRequest, CancellationToken)` — your business logic
5. **Serialization**: `TResponse` → `Dictionary<string, object?>`
6. **Filtering**: only the declared returns are included in the response (see Step 2)
7. **Outcome**: a `success` boolean in the response decides the call's success; when it is
   `false`, the `error` string (or the `errors` list) becomes the error the agent sees. A
   response without `success` counts as a success.

An exception thrown by `ExecuteTypedAsync` is not fatal either: `CallAsync` turns it into a
failed response (`Tool execution failed: …`), and a cancellation into `Operation cancelled`.

### Complete example — Simple tool (no external dependency)

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

        return null; // Valid
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

For a tool calling an external API, use `HttpToolBase<TRequest, TResponse>`, which provides a shared `HttpClient` and URL validation — which your code calls (`ValidateUrlAsync`); nothing validates a URL behind your back.

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

    // Simple ctor (shared static HttpClient, redirects refused). To plug in the host's
    // IUrlValidator and HttpHeaderSanitizer, use the
    // base(IUrlValidator, HttpHeaderSanitizer, HttpClient?, ILogger?) ctor; without them
    // ValidateUrlAsync applies the fail-closed default guard.
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

            // SSRF check — mandatory as soon as any part of the URL comes from the agent
            var check = await ValidateUrlAsync(url, cancellationToken).ConfigureAwait(false);
            if (!check.IsAllowed)
                return new WeatherResponse { Success = false, Error = check.DenialReason };

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
// In an extension method or at startup
services.AddSingleton<IBaseTool>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetService<ILogger<WeatherTool>>();
    return new WeatherTool(
        apiKey: config["Weather:ApiKey"]!,
        logger: logger);
});
```

The tool is then available via `IEnumerable<IBaseTool>`. **Name resolution from YAML
needs a DI-backed registry**: the default `AddOrkeonInfrastructure()` registers the
*empty* `InMemoryToolRegistry` stub, which never sees your `IBaseTool` registrations —
the runner host swaps in `ServiceProviderToolRegistry` (`Orkeon.Hosting`), and an
embedding host must do the same:

```csharp
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();
```

### Option C — Via IToolRegistry

`IToolRegistry` (`Orkeon.Domain.Tools`; `ServiceProviderToolRegistry` in the runners, the `InMemoryToolRegistry` stub by default) enables dynamic registration (`RegisterToolAsync`) and resolution of tools by name:

```csharp
var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();
var tool = await toolRegistry.GetToolByNameAsync("weather");
```

## Step 6 — Internal composition pattern (FileToolBase / HttpToolBase)

`ToolBase<TRequest, TResponse>`, `FileToolBase<TRequest, TResponse>` and `HttpToolBase<TRequest, TResponse>` each use a composition pattern via a private inner class `ComponentPipeline` that inherits from `ComponentBase<TRequest, TResponse>` (Domain layer) — the specialized bases derive from the non-generic `FileToolBase`/`HttpToolBase`, not from `ToolBase<,>`, so each carries its own copy of the sealed pipeline.

This pattern lets the tool class access the typed pipeline's serialization/deserialization methods without inheriting directly from `ComponentBase` (which lives in the Domain layer and knows nothing about tool concepts).

### Pipeline architecture

```
ToolBase<TRequest, TResponse> (or FileToolBase<> / HttpToolBase<>)
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
    // serialization or validation (simplified: the real method also catches
    // JsonException → "Invalid parameters: …")
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
        // ... success/error propagation from the `success` / `error` / `errors` fields ...
        return new ProtocolToolCallResponse(Success: success, Result: filteredResult, Error: error);
    }

    // Your extension point — pure business logic
    protected abstract Task<TResponse> ExecuteTypedAsync(
        TRequest request, CancellationToken cancellationToken);

    // Inner composition class
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

### Serialization: snake_case and type coercion

The `JsonComponentSerializer` (`Orkeon.Infrastructure.Serialization`) uses `JsonNamingPolicy.SnakeCaseLower` and includes 10 tolerant converters to handle YAML values arriving as strings — the six below plus `TolerantEnumConverterFactory` (enum names matched case-insensitively, an unknown name answered with the list of valid ones), `UriTolerantConverter`, `ImmutableArrayEnumTolerantConverter<EdgeKind>` and `ImmutableArrayStringTolerantConverter`:

- `BoolTolerantConverter`: `"true"`/`"false"` (any case), `"1"`/`"0"` and numbers (non-zero → `true`); `"yes"` is **not** accepted
- `IntTolerantConverter`: `"42"` → `42`
- `LongTolerantConverter`: `"123456789"` → `123456789L`
- `DecimalInvariantConverter`: `"3.14"` → `3.14m` (culture-invariant)
- `DoubleInvariantConverter`: `"3.14"` → `3.14d`
- `RawObjectConverter`: unwraps `JsonElement` into native .NET types

This guarantees that YAML parameters (which are all strings at deserialization time) are correctly converted to the C# types expected by `TRequest`.

The serializer is process-wide: `ComponentBase.DefaultSerializer`. `AddOrkeonInfrastructure()`
sets it (`TrySetDefaultSerializer`, which never overwrites one a host already set); a process
that builds tools without it — a unit-test project, typically — sets it once itself (see
Step 8), or the first call throws `ComponentBase.DefaultSerializer has not been configured`.

### A file tool, the VFS way

`FileReadTool.cs` (`Orkeon.Tools.FileSystem`) is the reference: the request carries a
**virtual** path, the tool resolves it with the right it needs, and every byte goes through
`_fileSystemService`:

```csharp
[ToolContract("line_count", Description = "Count the lines of a text file.")]
public sealed class LineCountTool : FileToolBase<LineCountRequest, LineCountResponse>
{
    // FileReadTool also takes an IPathValidator (base(fs, pathValidator, logger)) for
    // defense in depth on the resolved physical path
    public LineCountTool(IFileSystemService fileSystemService, ILogger<LineCountTool>? logger = null)
        : base(fileSystemService, logger) { }

    public override ToolAccess Access => ToolAccess.Read;

    protected override async Task<LineCountResponse> ExecuteTypedAsync(
        LineCountRequest request, CancellationToken cancellationToken)
    {
        // Mount + rights check (FileAccessRights.Read); physical paths never leak
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

A writing tool asks for `FileAccessRights.Write` (or `Create`), and calls
`EnsureDirectoryExistsAsync(virtualPath, ct)` before creating a file in a new folder.

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
        // One IBaseTool registration per tool. NOT TryAddSingleton<IBaseTool, …>
        // twice: TryAdd keys on the SERVICE type, so the second call would be a
        // silent no-op. Tools with non-DI ctor args (WeatherTool's apiKey) need
        // the factory form.
        services.AddSingleton<IBaseTool>(sp => new WeatherTool(
            apiKey: sp.GetRequiredService<IConfiguration>()["Weather:ApiKey"]!,
            logger: sp.GetService<ILogger<WeatherTool>>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseTool, TranslateTool>());

        // Usable by name in YAML once a DI-backed IToolRegistry is registered
        // (ServiceProviderToolRegistry — see Option B above).
        return services;
    }
}
```

Usage in `Program.cs`:

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();
services.AddOrkeonMyPackageTools();  // Your custom tools
services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>(); // name resolution for YAML
```

## Step 8 — Testing the tool

Tool tests follow the repository conventions: **xUnit with its native assertions only**, no
mocking framework and no fluent-assertion library. Doubles are hand-written classes named
`Mock*`, `Fake*` or `Stub*` that implement the production interface, kept in a `Doubles/`
folder of the test project (reference: `tests/core/Orkeon.Infrastructure.Tests/Doubles/MockTaskRepository.cs`).

- **Serializer.** Each tool test project sets the component serializer once, in a module
  initializer (`TestModuleInitializer.cs`, as in `tests/tools/Orkeon.Tools.FileSystem.Tests/`):

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

- **File system.** `Orkeon.Tests.Shared` (`tests/shared/`) provides VFS doubles:
  `FakeFileSystemService` (in memory), `DiskBackedFileSystemService` and
  `PassThroughFileSystemService` (real disk, for tests only — see the VFS exception for tests),
  `ThrowingFileSystemService`.
- **Call the tool the way an agent does** — `CallAsync` with a `ToolCallRequest` whose
  parameters use the snake_case names — so the schema check, the defaults, the deserialization
  and the output filtering are all exercised:

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

A new built-in tool also has to appear in [the inventory](./inventory.md): the doc-claims gate
(`scripts/check-doc-claims.py`) fails when a tool name in the code is missing from it, and the
tool count on the front pages moves with it.

## Pattern recap

```
1. Pick the base class       →  ToolBase<> / FileToolBase<> / HttpToolBase<>
2. Define TRequest           →  Record with [FieldSchema] on each parameter
3. Define TResponse          →  Record with [ReturnSchema] on each returned field
4. Implement the class       →  [ToolContract("name", Description = …)], ExecuteTypedAsync, Access
5. (Optional) Validation     →  override ValidateTypedRequest
6. Register                  →  Builder / DI (+ ServiceProviderToolRegistry) / IToolRegistry
7. Test                      →  xUnit, hand-written doubles, CallAsync with snake_case parameters
```

The JSON schema is auto-generated by `ToolSchemaGenerator` from the attributes — no manual maintenance required.
