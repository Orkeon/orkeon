namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when a script-supplied builder configuration is invalid: missing required
/// fields, illegal value combinations, or contracts violated by the script author.
/// </summary>
public sealed class InvalidScriptException : Exception
{
    /// <inheritdoc />
    public InvalidScriptException() { }

    /// <inheritdoc />
    public InvalidScriptException(string message) : base(message) { }

    /// <inheritdoc />
    public InvalidScriptException(string message, Exception inner) : base(message, inner) { }
}
