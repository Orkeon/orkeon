using Orkeon.Application.Interfaces.Infrastructure.Serialization;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IYamlSerializer with call tracking and configurable results.
/// </summary>
public class MockYamlSerializer : IYamlSerializer
{
    private readonly Dictionary<Type, object> _deserializeResults = [];
    private object _dynamicDeserializeResult = new Dictionary<string, object>();
    private string _serializeResult = "";

    // --- Tracking ---
    public int SerializeCallCount { get; private set; }
    public object? LastSerializedObject { get; private set; }

    public int DeserializeGenericCallCount { get; private set; }
    public string? LastDeserializedYaml { get; private set; }

    public int DeserializeDynamicCallCount { get; private set; }
    public string? LastDynamicDeserializedYaml { get; private set; }

    // --- Configuration ---
    public void SetSerializeResult(string result) => _serializeResult = result;

    public void SetDeserializeResult<T>(T result) where T : notnull
    {
        _deserializeResults[typeof(T)] = result;
    }

    public void SetDynamicDeserializeResult(object result) => _dynamicDeserializeResult = result;

    // --- IYamlSerializer ---
    public string Serialize<T>(T obj)
    {
        SerializeCallCount++;
        LastSerializedObject = obj;
        return _serializeResult;
    }

    public T Deserialize<T>(string yaml)
    {
        DeserializeGenericCallCount++;
        LastDeserializedYaml = yaml;

        if (_deserializeResults.TryGetValue(typeof(T), out var result))
            return (T)result;

        return default!;
    }

    public object Deserialize(string yaml)
    {
        DeserializeDynamicCallCount++;
        LastDynamicDeserializedYaml = yaml;
        return _dynamicDeserializeResult;
    }
}
