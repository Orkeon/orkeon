using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Common.CQRS;

/// <summary>
/// Represents the result of an operation.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
public record Result<T>
{
    /// <summary>
    /// Gets the value if the operation was successful.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    [SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static Result<T> Success(T value) => new() { Value = value, IsSuccess = true };

    /// <summary>
    /// Creates a failure result.
    /// </summary>
    [SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static Result<T> Failure(string error) => new() { Error = error, IsSuccess = false };
}
