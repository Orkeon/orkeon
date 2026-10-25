namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// An <see cref="IServiceProvider"/> that answers from a plain dictionary of services and does not
/// implement <c>IServiceProviderIsService</c>.
/// </summary>
public sealed class StubServiceProvider : IServiceProvider
{
    public Dictionary<Type, object> Services { get; } = [];

    public object? GetService(Type serviceType) => Services.GetValueOrDefault(serviceType);
}
