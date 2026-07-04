using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace Orkeon.Scripting.Versioning;

/// <summary>
/// Thrown when a <c>.ork.ts</c> declares a version not supported by the running runtime,
/// either via the <c>/// &lt;reference orkeon-script="X.Y" /&gt;</c> directive or programmatically.
/// </summary>
// S3376: name intentionally ends in "Error" to mirror the JS-surface `ScriptVersionMismatchError
// extends Error` declared in Typings/errors.d.ts; renaming would break the observable JS API.
[SuppressMessage("Major Code Smell", "S3376:Attribute, EventArgs, and Exception type names should end with the type being extended", Justification = "Name mirrors the JS-surface error class exposed to scripts.")]
[SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Name mirrors the JS-surface error class `ScriptVersionMismatchError extends Error` exposed to scripts; an `Exception` suffix would break the observable JS API.")]
[Serializable]
public sealed class ScriptVersionMismatchError : Exception
{
    /// <summary>Version declared by the script (or the host).</summary>
    public string DeclaredVersion { get; }

    /// <summary>Version supported by the current runtime.</summary>
    public string SupportedVersion { get; }

    /// <inheritdoc />
    public ScriptVersionMismatchError(string declaredVersion, string supportedVersion)
        : base($"Script declares orkeon-script version '{declaredVersion}' but the runtime supports '{supportedVersion}'.")
    {
        DeclaredVersion = declaredVersion;
        SupportedVersion = supportedVersion;
    }

    /// <inheritdoc />
    public ScriptVersionMismatchError()
    {
        DeclaredVersion = string.Empty;
        SupportedVersion = string.Empty;
    }

    /// <inheritdoc />
    public ScriptVersionMismatchError(string message) : base(message)
    {
        DeclaredVersion = string.Empty;
        SupportedVersion = string.Empty;
    }

    /// <inheritdoc />
    public ScriptVersionMismatchError(string message, Exception innerException) : base(message, innerException)
    {
        DeclaredVersion = string.Empty;
        SupportedVersion = string.Empty;
    }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private ScriptVersionMismatchError(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        DeclaredVersion = string.Empty;
        SupportedVersion = string.Empty;
    }
#pragma warning restore SYSLIB0051
}
