# Orkeon Plugin System — Technical Specification

> **Statut (R1.6, 2026-06-11)** : ce document est la **spécification d'origine**, conservée
> comme feuille de route. La **v1 réellement implémentée** est un sous-ensemble volontaire :
> contrat `IOrkeonPlugin`, découverte sur répertoire (VFS), chargement isolé par
> `AssemblyLoadContext` collectible + `AssemblyDependencyResolver`, activation DI opt-in
> `AddOrkeonPlugins(...)` — **sans** manifeste `plugin.json`, **sans** sandbox/permissions,
> **sans** hot-reload ni store de configuration par plugin (portée future). Voir
> [README.md](README.md) et `docs/architecture/plugins.md` pour l'état réel.

**Version**: 1.1.0-draft
**Date**: 2026-03-14
**Status**: Specification (future scope; v1 implements a subset — see README.md)
**Project**: `src/plugins/Orkeon.Plugins`

---

## 1. Purpose

The Plugin System provides runtime extensibility for Orkeon by allowing third-party developers to package and distribute custom tools as isolated, dynamically-loaded assemblies. Plugins are discovered, validated, sandboxed, configured, and registered without recompiling or restarting the host application.

### 1.1 Use Cases

- **Marketplace**: Distribute pre-built Orkeon tools as self-contained plugin packages.
- **Enterprise isolation**: Teams develop specialized tools independently; deployment is a folder drop.
- **SaaS extensibility**: Users upload plugin DLLs; the platform loads them at runtime with security constraints.
- **Development workflow**: Hot-reload plugins during development without restarting the host process.
- **Per-plugin configuration**: Each plugin manages its own settings (API keys, endpoints, thresholds) persisted independently of the host application.

### 1.2 Non-Goals

- Plugin-to-plugin communication (plugins are leaf components).
- UI extensibility (plugins provide tools only, not agent types or crew strategies).
- Remote plugin stores or auto-update mechanisms (out of scope for V1).
- Centralized secret management (plugins store their own secrets; integration with Azure Key Vault / AWS Secrets Manager is future scope).

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Host Application                                │
│                                                                         │
│  services.AddOrkeonPlugins(opts => { ... });                            │
│  await loader.DiscoverAndLoadPluginsAsync();                            │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────┐      │
│  │               Orkeon.Plugins Assembly                          │      │
│  │                                                               │      │
│  │  ┌──────────────────┐  ┌──────────────────────────────┐       │      │
│  │  │  PluginLoader     │  │  PluginSecurityManager       │       │      │
│  │  │  - Discover       │  │  - ValidatePermissions       │       │      │
│  │  │  - Load/Unload    │  │  - CreateSandbox             │       │      │
│  │  │  - Reload         │  │  - AllowPermission/Deny      │       │      │
│  │  └────────┬─────────┘  └──────────┬───────────────────┘       │      │
│  │           │                       │                           │      │
│  │  ┌────────▼─────────┐  ┌─────────▼──────────────────┐        │      │
│  │  │  PluginContext    │  │  PluginSandbox             │        │      │
│  │  │  - Assembly ref   │  │  - Permission checks       │        │      │
│  │  │  - Manifest copy  │  │  - Timeout enforcement     │        │      │
│  │  │  - Plugin ref     │  │  - Resource monitoring     │        │      │
│  │  │  - Configuration  │  └────────────────────────────┘        │      │
│  │  └────────┬─────────┘                                         │      │
│  │           │                                                   │      │
│  │  ┌────────▼──────────┐  ┌────────────────────────────┐        │      │
│  │  │ PluginLoadContext  │  │  IPluginConfigurationStore │        │      │
│  │  │  - Shared list     │  │  - Load / Save / Delete    │        │      │
│  │  │  - Dep resolver    │  │  - Per-plugin isolation    │        │      │
│  │  └───────────────────┘  └────────────────────────────┘        │      │
│  └───────────────────────────────────────────────────────────────┘      │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────┐      │
│  │  plugins/                                                      │      │
│  │  ├── my-plugin/                                                │      │
│  │  │   ├── plugin.json           (PluginManifest)                │      │
│  │  │   ├── config.json           (Plugin configuration — NEW)    │      │
│  │  │   ├── config.schema.json    (JSON Schema for config — NEW)  │      │
│  │  │   ├── MyPlugin.dll          (ToolPluginBase impl)           │      │
│  │  │   └── deps/                 (plugin-private dependencies)   │      │
│  │  └── another-plugin/                                           │      │
│  │      ├── plugin.json                                           │      │
│  │      └── AnotherPlugin.dll                                     │      │
│  └───────────────────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.1 Layer Placement

| Layer | Component | Role |
|---|---|---|
| Domain | `IBaseTool`, `IToolRegistry`, `ToolSchema`, `ToolCallRequest`, `ToolCallResponse` | Contracts that plugins must satisfy |
| Plugins | `IToolPlugin`, `PluginLoader`, `PluginSecurityManager`, `PluginSandbox`, `PluginManifest`, `IPluginConfigurationStore`, `PluginConfiguration` | Plugin lifecycle, security, isolation, configuration |
| Infrastructure | *(to implement)* `IToolRegistry` impl, `FilePluginConfigurationStore` | Bridge between PluginLoader and agent runtime; file-based config persistence |

### 2.2 Dependency Graph

```
Orkeon.Plugins
  ├── Orkeon.Domain
  ├── Microsoft.Extensions.DependencyInjection.Abstractions
  ├── Microsoft.Extensions.Logging.Abstractions
  └── Microsoft.Extensions.Options.ConfigurationExtensions
```

The Plugins project references **Domain only** (no Application dependency). Plugin tools implement `IBaseTool` from Domain.

---

## 3. Plugin Contract

### 3.1 IToolPlugin Interface

Every plugin must implement `IToolPlugin`. The abstract base class `ToolPluginBase` provides sensible defaults.

**Current state**: The interface exists in `Core/IToolPlugin.cs` with 10 members. This spec adds 2 new members (marked `// TO ADD`) that must be implemented in Step 4 of the Implementation Plan (section 16).

```csharp
namespace Orkeon.Plugins.Core;

public interface IToolPlugin
{
    // ── Metadata (EXISTING) ──
    string Name { get; }
    string Version { get; }
    string Author { get; }
    string Description { get; }
    string Category { get; }
    string? MinimumOrkeonVersion { get; }

    // ── Tool Discovery (EXISTING) ──
    IEnumerable<Type> GetToolTypes();

    // ── Security (EXISTING) ──
    IEnumerable<string> GetRequiredPermissions();

    // ── DI (EXISTING) ──
    void Configure(IServiceCollection services);

    // ── Configuration (TO ADD — section 16, Step 4) ──
    PluginConfigurationSchema GetConfigurationSchema();
    Task OnConfigurationChangedAsync(IPluginConfiguration configuration,
                                      CancellationToken ct = default);

    // ── Lifecycle (EXISTING) ──
    Task OnLoadAsync(CancellationToken cancellationToken = default);
    Task OnUnloadAsync(CancellationToken cancellationToken = default);
}
```

### 3.2 ToolPluginBase (Default Implementation)

| Member | Default Behavior |
|---|---|
| `Category` | `"General"` |
| `MinimumOrkeonVersion` | `null` (no constraint) |
| `GetRequiredPermissions()` | Empty (no permissions needed) |
| `Configure(IServiceCollection)` | Registers every type from `GetToolTypes()` as `Transient` |
| `GetConfigurationSchema()` | Returns `PluginConfigurationSchema.Empty` (no configuration) |
| `OnConfigurationChangedAsync()` | No-op |
| `OnLoadAsync` | No-op |
| `OnUnloadAsync` | No-op |

### 3.3 Tool Requirements

Each `Type` returned by `GetToolTypes()` must:

1. Implement `IBaseTool` (from `Orkeon.Domain.Tools`).
2. Provide a public parameterless constructor OR be resolvable via DI (registered in `Configure()`).
3. Expose `Name`, `Description`, `Schema` properties.
4. Implement both `CallAsync(ToolCallRequest)` (new JSON protocol) and `ExecuteAsync(string)` (legacy).

Recommended approach: inherit from `ToolBase<TRequest, TResponse>` (from `Orkeon.Tools.Abstractions`) which implements the full typed pipeline.

---

## 4. Plugin Manifest (`plugin.json`)

Every plugin directory must contain a `plugin.json` at its root. The manifest is deserialized into `PluginManifest` and validated before assembly loading.

### 4.1 Schema

```json
{
  "id": "string (required)",
  "name": "string (required)",
  "version": "string (required, semver)",
  "author": "string (optional)",
  "description": "string (optional)",
  "entryAssembly": "string (required, must end in .dll)",
  "pluginClass": "string (required, fully-qualified C# type name)",
  "minimumOrkeonVersion": "string (optional, semver)",
  "dependencies": [
    {
      "id": "string (required)",
      "minVersion": "string (required, semver)",
      "maxVersion": "string (optional, semver, must be >= minVersion)"
    }
  ],
  "permissions": ["string"],
  "configSchema": "string (optional, relative path to JSON Schema file)",
  "metadata": { "key": "value" }
}
```

### 4.2 Validation Rules

| Field | Rule |
|---|---|
| `id` | Required. Max 255 chars. Regex: `^[a-zA-Z0-9\-_.]+$` |
| `name` | Required. Non-empty, non-whitespace. |
| `version` | Required. Standard semver (`1.0.0`) or pre-release (`1.0.0-beta`). Validated via `System.Version.TryParse` on the part before `-`. |
| `entryAssembly` | Required. Must end with `.dll` (case-insensitive). |
| `pluginClass` | Required. Regex: `^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$` |
| `minimumOrkeonVersion` | Optional. If present, must be valid semver. |
| `dependencies[].id` | Required. No duplicates within the list. |
| `dependencies[].minVersion` | Required. Valid semver. |
| `dependencies[].maxVersion` | Optional. If present, must be valid semver and >= `minVersion`. |
| `author` | Optional. No validation (defaults to empty string). |
| `description` | Optional. No validation. |
| `configSchema` | Optional. If present, the file must exist relative to the plugin directory. |

### 4.3 Example

```json
{
  "id": "orkeon-weather-tools",
  "name": "Weather Tools",
  "version": "1.2.0",
  "author": "Orkeon Community",
  "description": "Weather forecast and historical data tools for Orkeon agents",
  "entryAssembly": "Orkeon.Tools.Weather.dll",
  "pluginClass": "Orkeon.Tools.Weather.WeatherPlugin",
  "minimumOrkeonVersion": "1.0.0",
  "dependencies": [],
  "permissions": ["Network.Access"],
  "configSchema": "config.schema.json",
  "metadata": {
    "category": "Data",
    "tags": ["weather", "forecast", "api"],
    "homepage": "https://github.com/orkeon-net/weather-tools"
  }
}
```

---

## 5. Plugin Configuration System (NEW)

### 5.1 Overview

Each plugin can declare, read, and write its own configuration. Configuration is:

- **Per-plugin isolated**: A plugin can only access its own configuration, never another plugin's.
- **Schema-validated**: Plugins declare a JSON Schema describing their configuration shape. Invalid writes are rejected.
- **Persisted**: Configuration survives host restarts. Default store is file-based (`config.json` alongside `plugin.json`).
- **Observable**: Plugins are notified when their configuration changes via `OnConfigurationChangedAsync`.

### 5.2 Configuration Schema

Plugins declare their configuration shape via `GetConfigurationSchema()`:

```csharp
namespace Orkeon.Plugins.Configuration;

/// <summary>
/// Describes the shape and defaults of a plugin's configuration.
/// </summary>
public class PluginConfigurationSchema
{
    /// <summary>Empty schema — plugin has no configuration.</summary>
    public static readonly PluginConfigurationSchema Empty = new();

    /// <summary>Configuration fields with type, description, default, and required flag.</summary>
    public IReadOnlyList<ConfigurationField> Fields { get; init; } = [];

    /// <summary>Optional JSON Schema document (from config.schema.json).</summary>
    public string? JsonSchemaDocument { get; init; }
}

public record ConfigurationField
{
    /// <summary>Dot-separated key path (e.g. "api.key", "cache.ttlSeconds").</summary>
    public required string Key { get; init; }

    /// <summary>JSON Schema type: "string", "integer", "number", "boolean", "array", "object".</summary>
    public required string Type { get; init; }

    /// <summary>Human-readable description for admin UIs and documentation.</summary>
    public string? Description { get; init; }

    /// <summary>Default value. Used when no explicit configuration is set.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>Whether this field must be set for the plugin to function.</summary>
    public bool Required { get; init; }

    /// <summary>Whether this field contains sensitive data (API keys, tokens).
    /// Sensitive fields are masked in logs and admin UIs.</summary>
    public bool Sensitive { get; init; }

    /// <summary>Allowed values (enum constraint).</summary>
    public IReadOnlyList<object>? AllowedValues { get; init; }
}
```

### 5.3 IPluginConfiguration (Read/Write API for Plugins)

Plugins interact with their configuration through this interface, injected via DI:

```csharp
namespace Orkeon.Plugins.Configuration;

/// <summary>
/// Provides typed access to plugin configuration.
/// Each plugin receives its own scoped instance — cross-plugin access is impossible.
/// </summary>
public interface IPluginConfiguration
{
    /// <summary>Get a configuration value by key. Returns default if not set.</summary>
    T? Get<T>(string key);

    /// <summary>Get a configuration value by key, with explicit fallback.</summary>
    T Get<T>(string key, T defaultValue);

    /// <summary>Try to get a configuration value. Returns false if not set.</summary>
    bool TryGet<T>(string key, out T? value);

    /// <summary>Set a configuration value. Validates against schema. Persists immediately.</summary>
    Task SetAsync<T>(string key, T value, CancellationToken ct = default);

    /// <summary>Set multiple values atomically.</summary>
    Task SetManyAsync(IReadOnlyDictionary<string, object?> values,
                       CancellationToken ct = default);

    /// <summary>Remove a configuration key. Fails if the key is marked Required in the schema.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Get all configuration as a flat dictionary.</summary>
    IReadOnlyDictionary<string, object?> GetAll();

    /// <summary>Check if a key exists.</summary>
    bool ContainsKey(string key);

    /// <summary>Bind the full configuration (or a section) to a strongly-typed POCO.</summary>
    T Bind<T>() where T : class, new();

    /// <summary>Bind a section of the configuration to a strongly-typed POCO.</summary>
    T BindSection<T>(string sectionPrefix) where T : class, new();
}
```

### 5.4 IPluginConfigurationStore (Persistence Backend)

The backing store is abstracted behind this interface, allowing file-based, database, or cloud-based implementations:

```csharp
namespace Orkeon.Plugins.Configuration;

/// <summary>
/// Persistence backend for plugin configurations.
/// Each method receives the pluginId — the store is responsible for isolation.
/// </summary>
public interface IPluginConfigurationStore
{
    /// <summary>Load all configuration for a plugin. Returns empty dict if none.</summary>
    Task<Dictionary<string, object?>> LoadAsync(string pluginId,
                                                  CancellationToken ct = default);

    /// <summary>Save the full configuration for a plugin (overwrite).</summary>
    Task SaveAsync(string pluginId, IReadOnlyDictionary<string, object?> values,
                    CancellationToken ct = default);

    /// <summary>Delete all configuration for a plugin.</summary>
    Task DeleteAsync(string pluginId, CancellationToken ct = default);

    /// <summary>Check if configuration exists for a plugin.</summary>
    Task<bool> ExistsAsync(string pluginId, CancellationToken ct = default);
}
```

### 5.5 FilePluginConfigurationStore (Default Implementation)

The default store persists configuration as `config.json` in each plugin's directory:

```
plugins/orkeon-weather-tools/
├── plugin.json
├── config.json            ← persisted configuration
├── config.schema.json     ← optional JSON Schema
└── Orkeon.Tools.Weather.dll
```

```csharp
namespace Orkeon.Plugins.Configuration;

/// <summary>
/// File-based configuration store. Each plugin's config lives in its own directory.
/// </summary>
public class FilePluginConfigurationStore : IPluginConfigurationStore
{
    private readonly string _pluginsDirectory;

    public FilePluginConfigurationStore(string pluginsDirectory);

    // config path = {_pluginsDirectory}/{pluginId}/config.json
    public Task<Dictionary<string, object?>> LoadAsync(string pluginId, CancellationToken ct);
    public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, object?> values, CancellationToken ct);
    public Task DeleteAsync(string pluginId, CancellationToken ct);
    public Task<bool> ExistsAsync(string pluginId, CancellationToken ct);
}
```

Behavior:
- **Load**: Reads and deserializes `config.json`. Returns empty dictionary if file doesn't exist.
- **Save**: Serializes to pretty-printed JSON. Creates file if it doesn't exist.
- **Delete**: Deletes `config.json`. Does not throw if file doesn't exist.
- **Sensitive values**: Stored in clear text in V1. Future versions may encrypt sensitive fields.

### 5.6 PluginConfiguration (Runtime Implementation)

The concrete implementation that wires schema validation, store access, and change notification:

```csharp
namespace Orkeon.Plugins.Configuration;

/// <summary>
/// Runtime configuration for a specific plugin.
/// Validates against schema on write, persists via store, notifies plugin on change.
/// </summary>
internal class PluginConfiguration : IPluginConfiguration
{
    private readonly string _pluginId;
    private readonly PluginConfigurationSchema _schema;
    private readonly IPluginConfigurationStore _store;
    private readonly IToolPlugin _plugin;
    private readonly ILogger _logger;
    private Dictionary<string, object?> _values;  // In-memory cache

    internal PluginConfiguration(
        string pluginId,
        PluginConfigurationSchema schema,
        IPluginConfigurationStore store,
        IToolPlugin plugin,
        ILogger logger);

    /// <summary>
    /// Called by PluginLoader ONCE after construction, before plugin.OnLoadAsync().
    /// 1. Loads values from store (config.json)
    /// 2. For each schema field with DefaultValue where key is missing → inserts default
    /// 3. Validates all Required fields are present (logs warning if missing)
    /// </summary>
    internal async Task InitializeAsync(CancellationToken ct);
}
```

**Initialization sequence** (inside `PluginLoader.LoadPluginAsync`, after assembly load):

```csharp
// 1. Get schema from plugin
var schema = plugin.GetConfigurationSchema();

// 2. Create configuration with store reference
var config = new PluginConfiguration(manifest.Id, schema, _configStore, plugin, _logger);

// 3. Load from disk + merge defaults (BEFORE OnLoadAsync)
await config.InitializeAsync(cancellationToken);

// 4. Now plugin can use config in OnLoadAsync
await plugin.OnLoadAsync(cancellationToken);

// 5. Create context with config
var context = new PluginContext(loadContext, assembly, plugin, pluginDirectory, manifest, config);
```

**Write flow** (`SetAsync` / `SetManyAsync`):

1. If schema has fields: validate key exists in schema.
2. Validate value type matches schema field type (string↔string, integer↔int/long, etc.).
3. If `AllowedValues` is set: validate value is in the list.
4. If `Required` is true: reject `RemoveAsync` on that key.
5. Update in-memory `_values` dictionary.
6. Persist full dictionary via `_store.SaveAsync(_pluginId, _values, ct)`.
7. Call `_plugin.OnConfigurationChangedAsync(this, ct)`.

**Type coercion rules** (for `Get<T>`):

| Schema Type | C# Types Accepted |
|---|---|
| `"string"` | `string` |
| `"integer"` | `int`, `long`, `short`, `byte` |
| `"number"` | `double`, `float`, `decimal` |
| `"boolean"` | `bool` |
| `"array"` | `List<T>`, `T[]`, `IEnumerable<T>` |
| `"object"` | `Dictionary<string, object?>`, POCO via `Bind<T>()` |

**`Bind<T>()` algorithm**:

1. Take all keys in `_values`.
2. Build a nested `Dictionary<string, object?>` by splitting keys on `"."`.
3. Serialize to JSON, then deserialize to `T` using `System.Text.Json`.
4. Example: `{"api.key": "abc", "api.baseUrl": "..."}` → `{"api": {"key": "abc", "baseUrl": "..."}}` → `T`.

### 5.7 Configuration in Plugin Lifecycle

The configuration is loaded **between assembly loading and plugin initialization**:

```
  1. DISCOVER        → Scan for plugin.json
  2. VALIDATE        → Deserialize + validate manifest
  3. CHECK PERMS     → SecurityManager validation
  4. LOAD ASSEMBLY   → AssemblyLoadContext + instantiate plugin
  5. LOAD CONFIG     → Load config.json, merge schema defaults   ← NEW
  6. INITIALIZE      → plugin.OnLoadAsync(ct) — config is available
  7. ACTIVE          → Plugin can read/write config at any time
  8. UNLOAD          → plugin.OnUnloadAsync(ct) — last chance to save
```

### 5.8 Plugin Developer Usage

#### Declaring Configuration Schema

```csharp
public class WeatherPlugin : ToolPluginBase
{
    public override PluginConfigurationSchema GetConfigurationSchema() => new()
    {
        Fields =
        [
            new ConfigurationField
            {
                Key = "api.key",
                Type = "string",
                Description = "OpenWeather API key",
                Required = true,
                Sensitive = true
            },
            new ConfigurationField
            {
                Key = "api.baseUrl",
                Type = "string",
                Description = "API base URL",
                DefaultValue = "https://api.openweathermap.org/data/2.5",
                Required = false
            },
            new ConfigurationField
            {
                Key = "cache.ttlSeconds",
                Type = "integer",
                Description = "Cache TTL in seconds",
                DefaultValue = 300,
                Required = false
            },
            new ConfigurationField
            {
                Key = "units",
                Type = "string",
                Description = "Temperature units",
                DefaultValue = "metric",
                AllowedValues = ["metric", "imperial", "standard"]
            }
        ]
    };
}
```

#### Reading Configuration in Tools

Tools receive `IPluginConfiguration` via DI (registered by the framework, not by the plugin):

```csharp
public class ForecastTool : ToolBase<ForecastRequest, ForecastResponse>
{
    private readonly IPluginConfiguration _config;
    private readonly HttpClient _httpClient;

    public ForecastTool(IPluginConfiguration config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    protected override async Task<ForecastResponse> ExecuteTypedAsync(
        ForecastRequest request, CancellationToken ct)
    {
        var apiKey = _config.Get<string>("api.key")
            ?? throw new InvalidOperationException("API key not configured");
        var baseUrl = _config.Get("api.baseUrl", "https://api.openweathermap.org/data/2.5");
        var units = _config.Get("units", "metric");

        var url = $"{baseUrl}/forecast?q={request.City}&appid={apiKey}&units={units}";
        var response = await _httpClient.GetFromJsonAsync<WeatherData>(url, ct);

        return new ForecastResponse
        {
            Summary = response!.Summary,
            Temperature = response.Main.Temp
        };
    }
}
```

#### Strongly-Typed Binding

```csharp
// Define a POCO matching your config shape
public class WeatherConfig
{
    public ApiSettings Api { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public string Units { get; set; } = "metric";

    public class ApiSettings
    {
        public string Key { get; set; } = "";
        public string BaseUrl { get; set; } = "https://api.openweathermap.org/data/2.5";
    }

    public class CacheSettings
    {
        public int TtlSeconds { get; set; } = 300;
    }
}

// In your tool or plugin
var config = _pluginConfig.Bind<WeatherConfig>();
var apiKey = config.Api.Key;
```

#### Writing Configuration (Admin / Setup)

```csharp
// During OnLoadAsync — set defaults or migrate
public override async Task OnLoadAsync(CancellationToken ct)
{
    var config = GetConfiguration();  // Helper on ToolPluginBase

    // Check if required config is present
    if (!config.ContainsKey("api.key"))
    {
        _logger.LogWarning("Weather plugin requires api.key configuration");
    }
}

// Programmatic config update (e.g., from an admin tool)
await config.SetAsync("cache.ttlSeconds", 600);
await config.SetManyAsync(new Dictionary<string, object?>
{
    ["api.key"] = "new-key-value",
    ["units"] = "imperial"
});
```

#### Reacting to Configuration Changes

```csharp
public override async Task OnConfigurationChangedAsync(
    IPluginConfiguration configuration, CancellationToken ct)
{
    // Rebuild HTTP client with new base URL
    var baseUrl = configuration.Get<string>("api.baseUrl");
    if (baseUrl != null)
    {
        _httpClientFactory.UpdateBaseAddress(baseUrl);
    }

    _logger.LogInformation("Weather plugin configuration updated");
}
```

### 5.9 config.json Example

```json
{
  "api.key": "sk-weather-abc123",
  "api.baseUrl": "https://api.openweathermap.org/data/2.5",
  "cache.ttlSeconds": 300,
  "units": "metric"
}
```

The file uses **flat dot-notation keys** matching the `ConfigurationField.Key` values. This is simpler to parse/validate than nested JSON and aligns with .NET's `IConfiguration` key convention.

### 5.10 config.schema.json Example (Optional)

Plugins may ship a standard JSON Schema file for external tooling (admin UIs, validation):

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "Weather Tools Configuration",
  "type": "object",
  "properties": {
    "api.key": {
      "type": "string",
      "description": "OpenWeather API key",
      "x-sensitive": true
    },
    "api.baseUrl": {
      "type": "string",
      "description": "API base URL",
      "default": "https://api.openweathermap.org/data/2.5"
    },
    "cache.ttlSeconds": {
      "type": "integer",
      "description": "Cache TTL in seconds",
      "default": 300,
      "minimum": 0
    },
    "units": {
      "type": "string",
      "description": "Temperature units",
      "default": "metric",
      "enum": ["metric", "imperial", "standard"]
    }
  },
  "required": ["api.key"]
}
```

---

## 6. Plugin Lifecycle

```
  ┌───────────┐
  │  On Disk   │  plugins/my-plugin/plugin.json + DLL + config.json
  └─────┬─────┘
        │ DiscoverAndLoadPluginsAsync()
        ▼
  ┌───────────────────────────────────────────────┐
  │ 1. DISCOVER                                    │
  │    Scan plugins/ for subdirs with plugin.json  │
  └─────┬─────────────────────────────────────────┘
        ▼
  ┌───────────────────────────────────────────────┐
  │ 2. VALIDATE MANIFEST                           │
  │    Deserialize + Validate() → error list       │
  │    Reject if any validation errors             │
  └─────┬─────────────────────────────────────────┘
        ▼
  ┌───────────────────────────────────────────────┐
  │ 3. CHECK PERMISSIONS                           │
  │    SecurityManager.ValidatePermissionsAsync()  │
  │    Reject if unauthorized permissions          │
  └─────┬─────────────────────────────────────────┘
        ▼
  ┌───────────────────────────────────────────────┐
  │ 4. LOAD ASSEMBLY                               │
  │    Create PluginLoadContext (collectible)       │
  │    LoadFromAssemblyPath(entryAssembly)          │
  │    Resolve pluginClass → check IToolPlugin     │
  │    Activator.CreateInstance() → plugin          │
  └─────┬─────────────────────────────────────────┘
        ▼
  ┌───────────────────────────────────────────────┐
  │ 5. LOAD CONFIGURATION                          │
  │    schema = plugin.GetConfigurationSchema()    │
  │    values = store.LoadAsync(pluginId)           │
  │    Merge schema defaults for missing keys      │
  │    Validate required fields                    │
  │    Create PluginConfiguration instance          │
  └─────┬─────────────────────────────────────────┘
        ▼
  ┌───────────────────────────────────────────────┐
  │ 6. INITIALIZE                                  │
  │    Register IPluginConfiguration in DI         │
  │    plugin.OnLoadAsync() — config available     │
  │    Create PluginContext (manifest + config)     │
  │    Add to _loadedPlugins[manifest.Id]          │
  └─────┬─────────────────────────────────────────┘
        ▼
  ┌───────────┐
  │   ACTIVE   │  GetAllToolTypes(), GetToolsByCategory()
  └─────┬─────┘   Plugin reads/writes config at will
        │ UnloadPluginAsync() or ReloadPluginAsync()
        ▼
  ┌───────────────────────────────────────────────┐
  │ 7. UNLOAD                                      │
  │    plugin.OnUnloadAsync()                      │
  │    PluginContext.Dispose()                      │
  │    PluginLoadContext.Unload() (GC collectible)  │
  │    Remove from _loadedPlugins                   │
  └───────────────────────────────────────────────┘
```

### 6.1 PluginLoader API

```csharp
public class PluginLoader : IDisposable
{
    // Constructor (EXISTING — add configStore parameter in Step 4)
    // Current:  PluginLoader(string pluginsDirectory, ILogger<PluginLoader> logger,
    //                        PluginSecurityManager? securityManager = null);
    // Target:
    PluginLoader(string pluginsDirectory, ILogger<PluginLoader> logger,
                 PluginSecurityManager? securityManager = null,
                 IPluginConfigurationStore? configStore = null);

    // Properties
    IReadOnlyDictionary<string, PluginContext> LoadedPlugins { get; }

    // Discovery & Loading
    Task<IEnumerable<PluginContext>> DiscoverAndLoadPluginsAsync(CancellationToken ct = default);
    Task<PluginContext?> LoadPluginAsync(string pluginDirectory, CancellationToken ct = default);

    // Unloading
    Task UnloadPluginAsync(string pluginId);
    Task<PluginContext?> ReloadPluginAsync(string pluginId, CancellationToken ct = default);

    // Tool Access
    IEnumerable<Type> GetAllToolTypes();
    IEnumerable<Type> GetToolsByCategory(string category);

    // Cleanup
    void Dispose();
}
```

### 6.2 PluginContext

```csharp
public class PluginContext : IDisposable
{
    IToolPlugin Plugin { get; }                    // EXISTING
    Assembly Assembly { get; }                     // EXISTING
    string PluginPath { get; }                     // EXISTING
    DateTime LoadedAt { get; }                     // EXISTING (UTC)
    PluginManifest Manifest { get; }               // EXISTING (defensive copy)
    IPluginConfiguration Configuration { get; }    // TO ADD in Step 4
}
```

**Step 4 changes to `PluginContext`**:
- Add `IPluginConfiguration Configuration` property.
- Update constructor to accept and store the configuration instance.
- Configuration is created by `PluginLoader` after loading the assembly and before calling `OnLoadAsync()`.

---

## 7. Assembly Isolation

### 7.1 PluginLoadContext

Each plugin is loaded in its own `AssemblyLoadContext` (collectible = true), enabling true unloading and garbage collection.

```csharp
public class PluginLoadContext : AssemblyLoadContext
{
    PluginLoadContext(string pluginPath, bool isCollectible = true);
}
```

### 7.2 Shared Assemblies

The following assemblies are **shared** with the default context (not isolated). This ensures the host and plugin use the same type identities for interfaces and DI:

| Assembly | Reason |
|---|---|
| `Orkeon.Domain` | `IBaseTool`, `ToolSchema`, `ToolCallRequest`, `ToolCallResponse` |
| `Orkeon.Application` | Application service interfaces |
| `Orkeon.Plugins` | `IToolPlugin`, `ToolPluginBase`, `PluginPermissions`, `IPluginConfiguration` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `IServiceCollection` |
| `Microsoft.Extensions.Logging.Abstractions` | `ILogger` |
| `System.Runtime` | BCL |
| `System.Collections` | BCL |
| `System.Linq` | BCL |

### 7.3 Plugin-Private Assemblies

All other assemblies (the plugin DLL itself and its dependencies) are loaded into the isolated context via `AssemblyDependencyResolver`. This prevents version conflicts between plugins.

### 7.4 Unmanaged DLLs

Native libraries are resolved via `LoadUnmanagedDllFromPath` if `AssemblyDependencyResolver` can locate them, enabling plugins with native dependencies.

---

## 8. Security Model

### 8.1 Permission System

8 standard permissions are defined as constants in `PluginPermissions`:

| Permission | Constant | Description |
|---|---|---|
| `FileSystem.Read` | `PluginPermissions.FileSystemRead` | Read files from disk |
| `FileSystem.Write` | `PluginPermissions.FileSystemWrite` | Write files to disk |
| `Network.Access` | `PluginPermissions.NetworkAccess` | Make HTTP/TCP requests |
| `Process.Execute` | `PluginPermissions.ProcessExecute` | Spawn child processes |
| `Environment.Variables` | `PluginPermissions.EnvironmentVariables` | Read environment variables |
| `System.Information` | `PluginPermissions.SystemInformation` | Read OS/hardware info |
| `Database.Access` | `PluginPermissions.DatabaseAccess` | Connect to databases |
| `Api.Access` | `PluginPermissions.ApiAccess` | Call external APIs |

### 8.2 Default Allowed Permissions

At startup, only 3 permissions are allowed by default:

- `FileSystem.Read`
- `Network.Access`
- `System.Information`

Additional permissions must be explicitly granted via `AllowPluginPermission()` or by adding to `PluginOptions.AllowedPermissions`.

### 8.3 PluginSecurityManager

```csharp
public class PluginSecurityManager
{
    PluginSecurityManager(ILogger logger, IEnumerable<string>? allowedPermissions = null);

    // Validation (called during plugin loading)
    Task<bool> ValidatePermissionsAsync(IEnumerable<string> requestedPermissions);

    // Sandbox management
    PluginSandbox CreateSandbox(string pluginId, IEnumerable<string> permissions);
    PluginSandbox? GetSandbox(string pluginId);
    void RemoveSandbox(string pluginId);

    // Runtime permission management
    void AllowPermission(string permission);
    void DenyPermission(string permission);
    IReadOnlyCollection<string> GetAllowedPermissions();
}
```

**Behavior**: If a plugin's manifest declares permissions not in the allowed set, `ValidatePermissionsAsync` returns `false` and the plugin is **rejected at load time**.

### 8.4 PluginSandbox

Per-plugin execution context providing runtime enforcement:

```csharp
public class PluginSandbox : IDisposable
{
    string PluginId { get; }
    IReadOnlyCollection<string> Permissions { get; }

    // Permission checks
    bool HasPermission(string permission);        // Case-sensitive
    void ValidateFileAccess(string path, bool write = false);
    void ValidateNetworkAccess(string url);

    // Sandboxed execution
    Task<TResult> ExecuteAsync<TResult>(
        Func<Task<TResult>> action,
        string operationName,
        TimeSpan? timeout = null);              // Default: 5 minutes

    // Monitoring
    ResourceUsageStats GetResourceUsage();
}
```

### 8.5 Resource Monitoring

The `ResourceMonitor` (internal) tracks per-operation metrics:

| Metric | Source |
|---|---|
| Memory used (bytes) | `GC.GetTotalMemory()` delta between start/end |
| CPU time (ms) | `Process.GetCurrentProcess().TotalProcessorTime` delta |
| Operation count | Number of `ExecuteAsync` calls |

Exposed via `ResourceUsageStats`:

```csharp
public class ResourceUsageStats
{
    long TotalMemoryUsedBytes { get; init; }
    long TotalCpuTimeMs { get; init; }
    int OperationCount { get; init; }
}
```

---

## 9. Metadata Attributes

Two attributes enable compile-time metadata annotation (complementing the runtime `plugin.json`):

### 9.1 ToolPluginAttribute (Assembly-level)

```csharp
[AttributeUsage(AttributeTargets.Assembly)]
public class ToolPluginAttribute : Attribute
{
    string Name { get; }
    string Version { get; }
    string? Author { get; set; }
    string? Description { get; set; }
}
```

Usage:
```csharp
[assembly: ToolPlugin("Weather Tools", "1.2.0", Author = "Orkeon Community")]
```

### 9.2 ToolMetadataAttribute (Class-level)

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class ToolMetadataAttribute : Attribute
{
    string Category { get; set; }                // Default: "General"
    string? RequiredPermissions { get; set; }     // Comma-separated
    string? SupportedAgents { get; set; }         // Comma-separated
    bool RequiresInternet { get; set; }           // Default: false
    bool RequiresFileSystem { get; set; }         // Default: false
    int MaxExecutionTimeSeconds { get; set; }     // Default: 300
    int MemoryLimitMB { get; set; }               // Default: 512

    IEnumerable<string> GetRequiredPermissionsList();
    IEnumerable<string> GetSupportedAgentsList();
}
```

---

## 10. Dependency Injection Integration

### 10.1 Registration

```csharp
services.AddOrkeonPlugins(options =>
{
    options.PluginsDirectory = "plugins";       // Default: "plugins"
    options.EnableHotReload = true;             // Default: true
    options.ValidateSignatures = false;         // Default: false (future)
    options.MaxMemoryPerPluginMB = 512;         // Default: 512
    options.MaxExecutionTimeSeconds = 300;      // Default: 300
});
```

### 10.2 Registered Services

| Service | Lifetime | Description |
|---|---|---|
| `PluginLoader` | Singleton | Main plugin lifecycle manager |
| `PluginSecurityManager` | Singleton | Permission validation and sandbox factory |
| `IPluginConfigurationStore` | Singleton | Configuration persistence backend (NEW) |

### 10.3 PluginOptions

```csharp
public class PluginOptions
{
    string PluginsDirectory { get; set; }              // "plugins"
    HashSet<string> AllowedPermissions { get; }        // Read-only property; 3 defaults pre-populated
    bool EnableHotReload { get; set; }                 // true
    bool ValidateSignatures { get; set; }              // false (future)
    int MaxMemoryPerPluginMB { get; set; }             // 512
    int MaxExecutionTimeSeconds { get; set; }          // 300
}
```

> **Note**: `AllowedPermissions` is a read-only `HashSet<string>`. Use `.Add()` inside the lambda or the fluent `AllowPluginPermission()` extension. Direct assignment is not supported.

### 10.4 Fluent Permission API

```csharp
// AllowPluginPermission returns IServiceCollection — each call is independent
services.AddOrkeonPlugins();
services.AllowPluginPermission(PluginPermissions.FileSystemWrite);
services.AllowPluginPermission(PluginPermissions.DatabaseAccess);
```

### 10.5 Plugin-Side DI

Plugins register their own services via `Configure(IServiceCollection)`:

```csharp
public override void Configure(IServiceCollection services)
{
    base.Configure(services);   // Registers all tool types as Transient
    services.AddSingleton<IWeatherApiClient, WeatherApiClient>();
}
```

The framework automatically registers `IPluginConfiguration` scoped to this plugin before calling `Configure()`. Plugin tools can inject it directly.

---

## 11. Integration with Existing Tool System

### 11.1 Current Tool Architecture

The existing tool system uses a typed pipeline:

```
Dict<string,object> → NormalizeParameters → Deserialize<TReq> → Validate → ExecuteTyped → Serialize<TRes> → Dict<string,object>
```

Base classes (in `Orkeon.Tools.Abstractions`):

| Base Class | Purpose |
|---|---|
| `ToolBase` | Non-generic. Reads `[ToolContract]` attributes, parameter validation, JSON parsing. |
| `ToolBase<TReq, TRes>` | Generic. Full typed pipeline via internal `ComponentBase<TReq, TRes>`. YAML defaults merging. |
| `FileToolBase<TReq, TRes>` | + `IPathValidator` integration |
| `HttpToolBase<TReq, TRes>` | + `IUrlValidator`, `HttpClient`, header sanitization |

### 11.2 What Plugin Tools Must Implement

Plugin tools implement `IBaseTool` from Domain:

```csharp
public interface IBaseTool
{
    string Name { get; }
    string Description { get; }
    ToolSchema Schema { get; }
    Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken ct = default);
    Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default);
    bool ValidateInput(string input);
}
```

Recommended: Plugin tools inherit `ToolBase<TReq, TRes>` from `Orkeon.Tools.Abstractions` to get the full pipeline. This requires the plugin project to reference both `Orkeon.Domain` and `Orkeon.Tools.Abstractions`.

### 11.3 IToolRegistry — The Missing Bridge

`IToolRegistry` (in Domain) defines how tools are discovered at runtime by agents:

```csharp
public interface IToolRegistry
{
    Task<bool> RegisterToolAsync(IBaseTool tool);
    Task<bool> UnregisterToolAsync(string toolId);
    Task<IBaseTool?> GetToolAsync(string toolId);
    Task<IBaseTool?> GetToolByNameAsync(string name);
    Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync();
    Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags);
    Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability);
    Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools);
    Task<bool> IsRegisteredAsync(string toolId);
    Task ClearAsync();
}
```

**Known issue**: `GetToolsAsync(IEnumerable<ITool>)` references `ITool`. While `ITool` exists as a type alias for `IBaseTool` in Domain, the plugin system's `IToolPlugin.GetToolTypes()` returns `Type` objects rather than `ITool` instances. The integration layer must bridge this gap by instantiating tool types and registering them.

### 11.4 Required Integration Work (P1 — after Configuration)

To make plugins functional end-to-end:

1. **Implement `IToolRegistry`** in Infrastructure that wraps DI container + plugin loader.
2. **After loading plugins**, iterate `PluginLoader.GetAllToolTypes()`. For each type, build a temporary `ServiceCollection`, call `plugin.Configure(services)`, build a `ServiceProvider`, resolve the tool instance, and register it in `IToolRegistry`.
3. **Wire `IToolRegistry`** into agent execution pipeline (where agents select and invoke tools).
4. **Handle hot-reload**: On `ReloadPluginAsync()`, unregister old tools, re-register new ones.

---

## 12. Plugin Developer Guide

### 12.1 Project Setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="Orkeon.Domain.csproj" />
    <ProjectReference Include="Orkeon.Plugins.csproj" />
    <!-- Optional: for ToolBase<TReq, TRes> -->
    <ProjectReference Include="Orkeon.Tools.Abstractions.csproj" />
  </ItemGroup>
</Project>
```

### 12.2 Implement the Plugin Class

```csharp
using Orkeon.Plugins.Core;
using Orkeon.Plugins.Configuration;

[assembly: ToolPlugin("Weather Tools", "1.2.0", Author = "Orkeon Community")]

public class WeatherPlugin : ToolPluginBase
{
    public override string Name => "Weather Tools";
    public override string Version => "1.2.0";
    public override string Author => "Orkeon Community";
    public override string Description => "Weather forecast and historical data";
    public override string Category => "Data";
    public override string? MinimumOrkeonVersion => "1.0.0";

    public override IEnumerable<Type> GetToolTypes()
    {
        yield return typeof(ForecastTool);
        yield return typeof(HistoricalWeatherTool);
    }

    public override IEnumerable<string> GetRequiredPermissions()
    {
        yield return PluginPermissions.NetworkAccess;
    }

    public override PluginConfigurationSchema GetConfigurationSchema() => new()
    {
        Fields =
        [
            new ConfigurationField { Key = "api.key", Type = "string",
                Required = true, Sensitive = true,
                Description = "OpenWeather API key" },
            new ConfigurationField { Key = "api.baseUrl", Type = "string",
                DefaultValue = "https://api.openweathermap.org/data/2.5",
                Description = "API base URL" },
            new ConfigurationField { Key = "cache.ttlSeconds", Type = "integer",
                DefaultValue = 300,
                Description = "Cache TTL in seconds" },
            new ConfigurationField { Key = "units", Type = "string",
                DefaultValue = "metric",
                AllowedValues = ["metric", "imperial", "standard"],
                Description = "Temperature units" }
        ]
    };

    public override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        services.AddHttpClient<ForecastTool>();
    }

    public override async Task OnLoadAsync(CancellationToken ct)
    {
        // Config is available here — validate API key presence
    }

    public override async Task OnConfigurationChangedAsync(
        IPluginConfiguration configuration, CancellationToken ct)
    {
        // React to config changes at runtime
    }
}
```

### 12.3 Implement Tools (Typed Pattern)

```csharp
using Orkeon.Domain.Attributes;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Plugins.Configuration;

public sealed record ForecastRequest
{
    [FieldSchema(Description = "City name", Example = "Paris")]
    public string City { get; init; } = "";

    [FieldSchema(Description = "Number of forecast days", IsRequired = false)]
    public int Days { get; init; } = 5;
}

public sealed record ForecastResponse
{
    [ReturnSchema(Description = "Forecast summary")]
    public string Summary { get; init; } = "";

    [ReturnSchema(Description = "Temperature in Celsius")]
    public double Temperature { get; init; }
}

[ToolContract("weather_forecast", Name = "weather_forecast",
    Description = "Get weather forecast for a city", Category = "Data")]
[ToolMetadata(RequiresInternet = true, RequiredPermissions = "Network.Access")]
public class ForecastTool : ToolBase<ForecastRequest, ForecastResponse>
{
    private readonly IPluginConfiguration _config;
    private readonly HttpClient _httpClient;

    public ForecastTool(IPluginConfiguration config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    protected override async Task<ForecastResponse> ExecuteTypedAsync(
        ForecastRequest request, CancellationToken ct)
    {
        var apiKey = _config.Get<string>("api.key")
            ?? throw new InvalidOperationException("Weather API key not configured");
        var baseUrl = _config.Get("api.baseUrl",
                                   "https://api.openweathermap.org/data/2.5");
        var units = _config.Get("units", "metric");

        var url = $"{baseUrl}/forecast?q={request.City}&cnt={request.Days}" +
                  $"&appid={apiKey}&units={units}";

        var data = await _httpClient.GetFromJsonAsync<WeatherApiResponse>(url, ct);

        return new ForecastResponse
        {
            Summary = data!.Description,
            Temperature = data.Main.Temp
        };
    }
}
```

### 12.4 Create Manifest

```json
{
  "id": "orkeon-weather-tools",
  "name": "Weather Tools",
  "version": "1.2.0",
  "author": "Orkeon Community",
  "description": "Weather forecast and historical data tools",
  "entryAssembly": "Orkeon.Tools.Weather.dll",
  "pluginClass": "Orkeon.Tools.Weather.WeatherPlugin",
  "minimumOrkeonVersion": "1.0.0",
  "permissions": ["Network.Access"],
  "configSchema": "config.schema.json",
  "metadata": {
    "category": "Data",
    "tags": ["weather", "forecast"]
  }
}
```

### 12.5 Ship Default Configuration

```json
// config.json (optional — shipped with the plugin or created by admin)
{
  "api.baseUrl": "https://api.openweathermap.org/data/2.5",
  "cache.ttlSeconds": 300,
  "units": "metric"
}
```

### 12.6 Deploy

```
plugins/
└── orkeon-weather-tools/
    ├── plugin.json
    ├── config.json              ← plugin configuration
    ├── config.schema.json       ← optional JSON Schema
    ├── Orkeon.Tools.Weather.dll
    └── (dependency DLLs)
```

---

## 13. Host Application Integration

### 13.1 Startup Configuration

```csharp
var builder = Host.CreateApplicationBuilder();

// Register core Orkeon services
builder.Services.AddOrkeonInfrastructure();

// Register plugin system with configuration store
builder.Services.AddOrkeonPlugins(options =>
{
    options.PluginsDirectory = "plugins";
    options.EnableHotReload = true;
    options.MaxMemoryPerPluginMB = 256;
    options.AllowedPermissions.Add(PluginPermissions.FileSystemWrite);
});

var app = builder.Build();

// Load all plugins (includes config loading)
var loader = app.Services.GetPluginLoader();
var plugins = await loader.DiscoverAndLoadPluginsAsync();

// Register plugin tools in IToolRegistry (future — requires implementation)
var registry = app.Services.GetRequiredService<IToolRegistry>();
foreach (var toolType in loader.GetAllToolTypes())
{
    if (app.Services.GetService(toolType) is IBaseTool tool)
        await registry.RegisterToolAsync(tool);
}
```

### 13.2 Hot Reload

```csharp
// Reload a specific plugin (config is reloaded from disk)
await loader.ReloadPluginAsync("orkeon-weather-tools");

// Re-register updated tools in registry
// (requires un-registering old tools first)
```

### 13.3 Admin Configuration Update

```csharp
// Update a plugin's configuration programmatically
var context = loader.LoadedPlugins["orkeon-weather-tools"];
await context.Configuration.SetAsync("cache.ttlSeconds", 600);

// Or via the store directly (for bulk operations)
var store = app.Services.GetRequiredService<IPluginConfigurationStore>();
await store.SaveAsync("orkeon-weather-tools", new Dictionary<string, object?>
{
    ["api.key"] = "new-api-key",
    ["cache.ttlSeconds"] = 600
});
```

### 13.4 Cleanup

```csharp
// PluginLoader.Dispose() unloads all plugins and their AssemblyLoadContexts
loader.Dispose();
```

---

## 14. Current State & Known Issues

### 14.1 What Exists (Implemented)

| Component | Status | Test Coverage |
|---|---|---|
| `IToolPlugin` + `ToolPluginBase` | Complete | ~15 tests |
| `PluginManifest` + validation | Complete | ~50 tests |
| `PluginLoader` | Complete | ~30 tests |
| `PluginLoadContext` | Complete | ~15 tests |
| `PluginContext` | Complete | ~30 tests |
| `PluginSecurityManager` | Complete | ~35 tests |
| `PluginSandbox` + `ResourceMonitor` | Complete | ~40 tests |
| `PluginServiceExtensions` + `PluginOptions` | Complete | ~35 tests |
| `PluginAttributes` | Complete | ~30 tests |
| README.md | Outdated (see 14.3) | — |
| **Total** | **~9 source files** | **~280 tests** |

### 14.2 What Must Be Implemented

| Component | Description | Priority |
|---|---|---|
| **`IPluginConfiguration`** | Read/write API for plugins (section 5.3) | **P0** |
| **`IPluginConfigurationStore`** | Persistence abstraction (section 5.4) | **P0** |
| **`FilePluginConfigurationStore`** | File-based default store (section 5.5) | **P0** |
| **`PluginConfiguration`** | Runtime implementation with validation (section 5.6) | **P0** |
| **`PluginConfigurationSchema` + `ConfigurationField`** | Schema declaration types (section 5.2) | **P0** |
| **`IToolPlugin` extension** | Add `GetConfigurationSchema()` + `OnConfigurationChangedAsync()` | **P0** |
| **`PluginLoader` update** | Add config loading step between assembly load and initialize | **P0** |
| **`PluginContext` update** | Add `Configuration` property | **P0** |
| **`IToolRegistry` implementation** | Infrastructure adapter bridging plugins → agents | **P1** |
| **`IToolRegistry` fix** | Fix `GetToolsAsync(IEnumerable<ITool>)` — `ITool` doesn't exist | **P1** |
| **DI bridge** | Register plugin tool types in host container after loading | **P1** |
| **Infrastructure call** | Wire `PluginLoader` into `AddOrkeonInfrastructure()` | **P1** |

### 14.3 Known Issues in Existing Code

| Issue | Description | Severity |
|---|---|---|
| **README inconsistencies** | References `AddPluginToolRegistry()`, `LoadPluginToolsAsync()`, `PluginToolRegistry` — none exist | Minor |
| **README field names** | Uses `mainAssembly` and `requiredPermissions`; actual schema uses `entryAssembly` and `permissions` | Minor |
| **README target framework** | Shows `net9.0`; actual project uses `net10.0` | Minor |
| **No signature validation** | `PluginOptions.ValidateSignatures` is declared but never enforced | Future |
| **No resource limit enforcement** | `MaxMemoryPerPluginMB` and `MaxExecutionTimeSeconds` in options are not passed to sandboxes | Future |
| **No dependency resolution** | `PluginManifest.Dependencies` are validated but inter-plugin dependency loading is not implemented | Future |
| **Linux path issues** | Some file-path tests may fail on Linux due to path separator assumptions | Minor |

### 14.4 Build Status

- **Compiles**: Yes (net10.0, all NuGet packages resolve).
- **Tests**: 280+ tests exist. Pass on Windows; some path-related tests may fail on Linux.
- **Integration**: Zero integration with any other project in the solution.

---

## 15. File Inventory

### 15.1 Current Files

```
src/plugins/Orkeon.Plugins/
├── Orkeon.Plugins.csproj
├── README.md
├── Core/
│   ├── IToolPlugin.cs              # IToolPlugin interface + ToolPluginBase
│   ├── PluginAttributes.cs         # ToolPluginAttribute, ToolMetadataAttribute, PluginPermissions
│   ├── PluginContext.cs            # PluginLoadContext + PluginContext
│   └── PluginLoader.cs            # Main loader (discover, load, unload, reload)
├── Registry/
│   └── PluginManifest.cs          # Manifest model + validation + JSON serialization
├── Security/
│   ├── PluginSandbox.cs           # Sandbox + ResourceMonitor + ResourceUsageStats
│   └── PluginSecurityManager.cs   # Permission management + sandbox factory
└── DependencyInjection/
    └── PluginServiceExtensions.cs # AddOrkeonPlugins() + PluginOptions
```

### 15.2 Files to Create (Configuration System)

```
src/plugins/Orkeon.Plugins/
└── Configuration/                          # NEW directory
    ├── IPluginConfiguration.cs             # Read/write API interface
    ├── IPluginConfigurationStore.cs        # Persistence backend interface
    ├── PluginConfiguration.cs              # Runtime implementation
    ├── PluginConfigurationSchema.cs        # Schema + ConfigurationField
    └── FilePluginConfigurationStore.cs     # File-based store implementation

tests/plugins/Orkeon.Plugins.Tests/
└── Configuration/                          # NEW directory
    ├── PluginConfigurationTests.cs         # Read/write/validate tests
    ├── PluginConfigurationSchemaTests.cs   # Schema declaration tests
    ├── FilePluginConfigurationStoreTests.cs # File persistence tests
    └── ConfigurationIntegrationTests.cs    # End-to-end config lifecycle
```

### 15.3 Current Test Files

```
tests/plugins/Orkeon.Plugins.Tests/
├── Orkeon.Plugins.Tests.csproj
├── Core/
│   ├── PluginAttributesTests.cs
│   ├── PluginContextTests.cs
│   ├── PluginLoaderEdgeCasesTests.cs
│   ├── PluginLoaderTests.cs
│   ├── PluginManifestTests.cs
│   ├── SimplePluginTests.cs
│   └── ToolPluginBaseTests.cs
├── Registry/
│   └── PluginManifestValidationTests.cs
├── Security/
│   ├── PluginSandboxTests.cs
│   ├── PluginSecurityManagerTests.cs
│   └── PluginSecurityManagerEdgeCaseTests.cs
├── DependencyInjection/
│   └── PluginServiceExtensionsTests.cs
├── Helpers/
│   └── TestHelpers.cs
└── TestDoubles/
    ├── TestLogger.cs
    ├── TestToolPlugin.cs
    ├── TestPluginLoadContext.cs
    └── TestFileSystem.cs
```

---

## 16. Implementation Plan

This plan is ordered for Claude Code execution. Each step is independently buildable and testable.

### Step 1: Configuration Types (no dependencies on existing code)

Create `src/plugins/Orkeon.Plugins/Configuration/`:

1. `PluginConfigurationSchema.cs` — `PluginConfigurationSchema` + `ConfigurationField` records.
2. `IPluginConfiguration.cs` — Read/write interface.
3. `IPluginConfigurationStore.cs` — Persistence interface.

**Verify**: `dotnet build src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj`

### Step 2: File-Based Store

4. `FilePluginConfigurationStore.cs` — JSON file read/write with `System.Text.Json`.

**Verify**: `dotnet build` + write `FilePluginConfigurationStoreTests.cs`

### Step 3: Runtime Configuration

5. `PluginConfiguration.cs` — In-memory cache, schema validation, store delegation, change notification.

**Verify**: `dotnet build` + write `PluginConfigurationTests.cs` + `PluginConfigurationSchemaTests.cs`

### Step 4: Wire into Plugin Contract

6. **`Core/IToolPlugin.cs`**: Add 2 methods to `IToolPlugin` interface:
   ```csharp
   PluginConfigurationSchema GetConfigurationSchema();
   Task OnConfigurationChangedAsync(IPluginConfiguration configuration, CancellationToken ct = default);
   ```
   Add `using Orkeon.Plugins.Configuration;` at top.

7. **`Core/IToolPlugin.cs`**: Add default implementations in `ToolPluginBase`:
   ```csharp
   public virtual PluginConfigurationSchema GetConfigurationSchema()
       => PluginConfigurationSchema.Empty;
   public virtual Task OnConfigurationChangedAsync(IPluginConfiguration configuration, CancellationToken ct)
       => Task.CompletedTask;
   ```

8. **`Core/PluginContext.cs`**: Add `IPluginConfiguration Configuration` property.
   Update constructor to accept `PluginConfiguration config` parameter.
   Store as readonly field, expose via property.

9. **`Core/PluginLoader.cs`**: In `LoadPluginAsync()`, insert config loading between
   `Activator.CreateInstance()` (line ~136) and `plugin.OnLoadAsync()` (line ~139):
   ```csharp
   var schema = plugin.GetConfigurationSchema();
   var config = new PluginConfiguration(manifest.Id, schema,
       _configStore ?? new FilePluginConfigurationStore(_pluginsDirectory),
       plugin, _logger);
   await config.InitializeAsync(cancellationToken);
   await plugin.OnLoadAsync(cancellationToken);
   var context = new PluginContext(loadContext, assembly, plugin, pluginDirectory, manifest, config);
   ```
   Add `_configStore` field + constructor parameter (optional, nullable).

**Verify**:
```bash
dotnet build src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj
dotnet test tests/plugins/Orkeon.Plugins.Tests/Orkeon.Plugins.Tests.csproj
```
Then write `tests/plugins/Orkeon.Plugins.Tests/Configuration/ConfigurationIntegrationTests.cs`.

### Step 5: DI Registration

10. **`DependencyInjection/PluginServiceExtensions.cs`**: In `AddOrkeonPlugins()`, register the config store:
    ```csharp
    services.AddSingleton<IPluginConfigurationStore>(
        new FilePluginConfigurationStore(options.PluginsDirectory));
    ```
    Update the `PluginLoader` factory registration to pass the store:
    ```csharp
    services.AddSingleton<PluginLoader>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<PluginLoader>>();
        var securityManager = new PluginSecurityManager(logger, options.AllowedPermissions);
        var configStore = sp.GetRequiredService<IPluginConfigurationStore>();
        return new PluginLoader(options.PluginsDirectory, logger, securityManager, configStore);
    });
    ```

11. **Plugin-side DI**: `IPluginConfiguration` is NOT registered in the host DI container.
    Instead, each `PluginConfiguration` instance is held by its `PluginContext`.
    Plugin tools that need config should receive it via their plugin's `Configure()`:
    ```csharp
    // In ToolPluginBase, after config is available:
    public override void Configure(IServiceCollection services)
    {
        base.Configure(services);
        // Framework registers the config instance before calling Configure:
        // services.AddSingleton<IPluginConfiguration>(this._configuration);
    }
    ```
    **Implementation detail**: `PluginLoader` calls `services.AddSingleton<IPluginConfiguration>(config)` BEFORE calling `plugin.Configure(services)`, so tools registered by the plugin can inject `IPluginConfiguration`.

**Verify**:
```bash
dotnet build src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj
dotnet test tests/plugins/Orkeon.Plugins.Tests/Orkeon.Plugins.Tests.csproj
```
Update `tests/plugins/Orkeon.Plugins.Tests/DependencyInjection/PluginServiceExtensionsTests.cs` with config store registration tests.

### Step 6: Update Manifest (Optional)

12. Add optional `configSchema` field to `PluginManifest`.
13. Update manifest validation.

**Verify**: `dotnet build` + `PluginManifestValidationTests.cs` updated

### Step 7: Fix README

14. Update `src/plugins/Orkeon.Plugins/README.md` to match actual API (fix field names, remove phantom methods, add configuration section).

---

## 17. Decision Record

### DR-01: Assembly Isolation via AssemblyLoadContext

**Decision**: Use `AssemblyLoadContext` (collectible) for plugin isolation.

**Rationale**: Provides type isolation, unloadability, and same-process performance. Shared assemblies ensure interface identity across boundaries.

### DR-02: Manifest-First Design

**Decision**: Require a `plugin.json` manifest validated before any assembly loading.

**Rationale**: Fail-fast on malformed plugins. Metadata available without executing code. Enables future tooling (stores, dependency graphs).

### DR-03: Permission-Based Security (Declarative)

**Decision**: Plugins declare required permissions; host declares allowed permissions.

**Rationale**: Principle of least privilege. Unauthorized plugins rejected at load time.

### DR-04: Domain-Only Dependency

**Decision**: `Orkeon.Plugins` depends on `Orkeon.Domain` only.

**Rationale**: Clean boundary. Plugins need `IBaseTool` and `ToolSchema` only.

### DR-05: Defensive Manifest Copying

**Decision**: `PluginContext` stores a deep copy of the manifest.

**Rationale**: Prevents mutation after validation.

### DR-06: Flat Key Configuration (NEW)

**Decision**: Plugin configuration uses flat dot-notation keys (`"api.key"`, `"cache.ttlSeconds"`) rather than nested JSON objects.

**Rationale**: Simpler to validate against schema fields. Aligns with .NET `IConfiguration` key convention (`section:key`). Flat keys are easier to merge, diff, and serialize. `Bind<T>()` handles the conversion to nested POCOs when needed.

### DR-07: Plugin-Scoped Configuration Isolation (NEW)

**Decision**: Each plugin gets its own `IPluginConfiguration` instance. Plugins cannot access another plugin's configuration.

**Rationale**: Security boundary. Prevents plugins from reading each other's API keys or secrets. The `IPluginConfigurationStore` receives the `pluginId` on every call, ensuring storage isolation.

### DR-08: Schema-Optional Configuration (NEW)

**Decision**: Plugins may declare a `PluginConfigurationSchema` with field definitions, but it is not required. Plugins with `PluginConfigurationSchema.Empty` can still use `IPluginConfiguration` as a free-form key-value store.

**Rationale**: Low barrier to entry for simple plugins. Schema validation adds safety for complex plugins with required API keys, but shouldn't be mandatory for plugins with no configuration.

---

*End of specification.*
