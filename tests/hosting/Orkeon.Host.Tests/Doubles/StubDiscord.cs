using System.Reflection;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// A Discord object with the properties a test names. The library's interfaces have dozens of members and its classes no
/// public constructor: everything else answers its default, and every call that is no property is recorded.
/// </summary>
internal class StubDiscord : DispatchProxy
{
    private readonly Dictionary<string, object?> _properties = new(StringComparer.Ordinal);
    private readonly List<(string Method, object?[] Arguments)> _calls = [];

    public static T Of<T>(params (string Name, object? Value)[] properties)
        where T : class
    {
        var stub = Create<T, StubDiscord>();
        foreach (var (name, value) in properties)
            ((StubDiscord)(object)stub)._properties[name] = value;
        return stub;
    }

    public static IReadOnlyList<(string Method, object?[] Arguments)> CallsOf(object stub) => ((StubDiscord)stub)._calls;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name.StartsWith("get_", StringComparison.Ordinal) && _properties.TryGetValue(targetMethod.Name[4..], out var value))
            return value;

        _calls.Add((targetMethod.Name, args ?? []));
        return targetMethod.ReturnType == typeof(Task<Discord.IUserMessage>) ? Task.FromResult<Discord.IUserMessage>(null!) : null;
    }
}
