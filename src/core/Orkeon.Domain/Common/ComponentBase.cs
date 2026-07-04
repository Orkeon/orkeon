namespace Orkeon.Domain.Common;

/// <summary>
/// Non-generic base class that holds the shared <see cref="IComponentSerializer"/>
/// default. Because <see cref="ComponentBase{TRequest,TResponse}"/> is generic,
/// a static field on that class would be duplicated per closed generic type.
/// This non-generic base keeps a single, shared default.
/// </summary>
public abstract class ComponentBase
{
    /// <summary>
    /// Thread-safe holder for the default serializer.
    /// Uses <see cref="Lazy{T}"/> with <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>
    /// to guarantee safe publication across threads without contention on reads.
    /// </summary>
    private static Lazy<IComponentSerializer?> s_defaultSerializer =
        new(() => null, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets or sets the default <see cref="IComponentSerializer"/> used by all
    /// ComponentBase instances that do not receive an explicit serializer.
    /// Must be set during application startup (typically via DI registration)
    /// before any ComponentBase instance is used.
    /// Thread-safe: reads use <see cref="Lazy{T}"/> and writes atomically replace the
    /// Lazy instance with a pre-computed value.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when attempting to read the default before it has been configured.
    /// </exception>
    public static IComponentSerializer DefaultSerializer
    {
        get
        {
            var serializer = s_defaultSerializer.Value;
            return serializer ?? throw new InvalidOperationException(
                "ComponentBase.DefaultSerializer has not been configured. " +
                "Call ComponentBase.DefaultSerializer = ... at startup, " +
                "or register IComponentSerializer in your DI container and use " +
                "AddOrkeonInfrastructure() to initialize it automatically.");
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            s_defaultSerializer = new Lazy<IComponentSerializer?>(
                () => value,
                LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    /// <summary>
    /// Returns true if a default serializer has been configured.
    /// </summary>
    public static bool IsDefaultSerializerConfigured => s_defaultSerializer.Value is not null;

    /// <summary>
    /// Sets the default serializer only if none has been configured yet, returning
    /// <c>true</c> when the value was applied and <c>false</c> when a serializer was
    /// already in place (which is then left untouched). This makes infrastructure
    /// registration idempotent and respectful of a serializer already posed by the host,
    /// avoiding a destructive overwrite from the DI composition path.
    /// </summary>
    /// <param name="serializer">The serializer to use as default if none exists.</param>
    /// <returns><c>true</c> if the serializer was set; <c>false</c> if one already existed.</returns>
    public static bool TrySetDefaultSerializer(IComponentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        if (s_defaultSerializer.Value is not null)
            return false;

        s_defaultSerializer = new Lazy<IComponentSerializer?>(
            () => serializer,
            LazyThreadSafetyMode.ExecutionAndPublication);
        return true;
    }
}

/// <summary>
/// Abstract base class that encapsulates the Dict-to-Model conversion pipeline.
/// Provides: Dict -> TRequest (deserialization with normalization and tolerant coercion)
/// and TResponse -> Dict (serialization with snake_case keys).
/// Delegates all serialization to <see cref="IComponentSerializer"/>, keeping
/// the Domain layer free of serialization implementation details.
/// </summary>
/// <typeparam name="TRequest">Strongly-typed request. Properties map to/from snake_case keys.</typeparam>
/// <typeparam name="TResponse">Strongly-typed response. Properties serialize to snake_case keys.</typeparam>
public abstract class ComponentBase<TRequest, TResponse> : ComponentBase
    where TRequest : class, new()
    where TResponse : class
{
    /// <summary>
    /// The serializer instance used by this component.
    /// </summary>
    private readonly IComponentSerializer? _serializer;

    /// <summary>
    /// Initializes a new instance using the static <see cref="ComponentBase.DefaultSerializer"/>.
    /// </summary>
    protected ComponentBase()
    {
    }

    /// <summary>
    /// Initializes a new instance with an explicit serializer (for testing or DI).
    /// </summary>
    /// <param name="serializer">The serializer to use for this component.</param>
    protected ComponentBase(IComponentSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializer = serializer;
    }

    /// <summary>
    /// The resolved serializer: prefers the instance injected via constructor,
    /// falls back to <see cref="ComponentBase.DefaultSerializer"/>.
    /// </summary>
    protected IComponentSerializer Serializer => _serializer ?? DefaultSerializer;

    /// <summary>
    /// Pure business logic — implement this instead of dealing with dictionaries.
    /// </summary>
    protected abstract Task<TResponse> ExecuteTypedAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Optional domain-level validation. Return null if valid, or an error message if invalid.
    /// </summary>
    protected virtual string? ValidateTypedRequest(TRequest request) => null;

    /// <summary>
    /// Deserializes a dictionary to TRequest using the configured serializer.
    /// </summary>
    protected TRequest DeserializeRequest(Dictionary<string, object?> parameters)
        => Serializer.Deserialize<TRequest>(parameters);

    /// <summary>
    /// Serializes TResponse to Dictionary&lt;string, object?&gt; using the configured serializer.
    /// </summary>
    protected Dictionary<string, object?> SerializeResponse(TResponse response)
        => Serializer.Serialize(response);

    /// <summary>
    /// Recursively normalizes parameter keys and unwraps implementation-specific value wrappers.
    /// </summary>
    protected static Dictionary<string, object?> NormalizeParameters(Dictionary<string, object?> parameters)
        => DefaultSerializer.NormalizeParameters(parameters);

    /// <summary>
    /// Recursively normalizes a value using the configured serializer.
    /// </summary>
    protected static object? NormalizeValue(object? value)
        => DefaultSerializer.NormalizeValue(value);

    /// <summary>
    /// Converts camelCase/PascalCase keys to snake_case. Preserves data keys
    /// (all-uppercase, all-lowercase, or already snake_case).
    /// </summary>
    protected static string NormalizeKeyToSnakeCase(string key)
        => DefaultSerializer.NormalizeKeyToSnakeCase(key);
}
