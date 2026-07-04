using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Default implementation of crew inputs.
/// </summary>
public class CrewInputs
{
    private CrewInputData _inputData;

    /// <summary>Gets the input data as a dictionary.</summary>
    public Dictionary<string, object> Data => _inputData.ToDictionary();

    /// <summary>Gets the strongly typed input data.</summary>
    public CrewInputData InputData => _inputData;

    /// <summary>Initializes a new instance of <see cref="CrewInputs"/> from a dictionary.</summary>
    /// <param name="data">Optional initial input data.</param>
    private CrewInputs(Dictionary<string, object>? data)
    {
        _inputData = CrewInputData.FromDictionary(data);
    }

    /// <summary>Initializes a new instance of <see cref="CrewInputs"/> from typed input data.</summary>
    /// <param name="inputData">The typed input data.</param>
    private CrewInputs(CrewInputData inputData)
    {
        _inputData = inputData ?? CrewInputData.Empty;
    }

    /// <summary>Creates an empty <see cref="CrewInputs"/> instance.</summary>
    public static CrewInputs Empty() => new((Dictionary<string, object>?)null);

    /// <summary>Creates a <see cref="CrewInputs"/> from a dictionary.</summary>
    /// <param name="data">Optional initial input data.</param>
    public static CrewInputs From(Dictionary<string, object>? data) => new(data);

    /// <summary>Creates a <see cref="CrewInputs"/> from typed input data.</summary>
    /// <param name="inputData">The typed input data.</param>
    public static CrewInputs From(CrewInputData inputData) => new(inputData);

    /// <summary>
    /// Gets a typed input value.
    /// </summary>
    /// <typeparam name="T">The type to cast the value to.</typeparam>
    /// <param name="key">The input key.</param>
    /// <returns>The typed value or default if not found.</returns>
    public T? GetValue<T>(string key)
    {
        return _inputData.GetValue<T>(key);
    }

    /// <summary>
    /// Checks if an input with the given key exists.
    /// </summary>
    /// <param name="key">The input key.</param>
    /// <returns>True if the key exists, false otherwise.</returns>
    public bool HasValue(string key)
    {
        return _inputData.HasValue(key);
    }

    /// <summary>
    /// Sets an input value.
    /// </summary>
    /// <param name="key">The input key.</param>
    /// <param name="value">The value to set.</param>
    public void SetValue(string key, object value)
    {
        _inputData = _inputData.SetValue(key, value);
    }
}
