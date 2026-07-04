namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IServiceProvider with call tracking and configurable service resolution.
/// </summary>
public class MockServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];

    // --- Tracking ---
    public int GetServiceCallCount { get; private set; }
    public Type? LastRequestedType { get; private set; }
    public List<Type> AllRequestedTypes { get; } = [];

    // --- Configuration ---

    /// <summary>
    /// Registers a service instance for a given type.
    /// </summary>
    public void Register<T>(T service) where T : notnull
    {
        _services[typeof(T)] = service;
    }

    /// <summary>
    /// Registers a service instance for a given type.
    /// </summary>
    public void Register(Type serviceType, object service)
    {
        _services[serviceType] = service;
    }

    /// <summary>
    /// Removes a registered service.
    /// </summary>
    public void Unregister<T>()
    {
        _services.Remove(typeof(T));
    }

    /// <summary>
    /// Clears all registered services.
    /// </summary>
    public void Clear() => _services.Clear();

    // --- IServiceProvider ---
    public object? GetService(Type serviceType)
    {
        GetServiceCallCount++;
        LastRequestedType = serviceType;
        AllRequestedTypes.Add(serviceType);

        _services.TryGetValue(serviceType, out var service);
        return service;
    }
}
