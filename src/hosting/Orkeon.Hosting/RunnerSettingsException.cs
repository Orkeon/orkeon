namespace Orkeon.Hosting;

/// <summary>
/// A setting the runner host refuses to be built on (GAP-35): a key that is no setting any more,
/// an LLM profile it cannot build, a value the configuration binder cannot convert, a settings
/// file it cannot read, an address that is no address. The message is the sentence the operator
/// fixes the setting with — it names the key, or the file and the place in it.
/// <para>
/// One type, so an entry point recognises a refused setting by its type rather than by its
/// message: <c>orkeon</c> answers it with one line and exit code 1, <c>orkeon-host</c> reports it
/// and exits 78. <see cref="RunnerHost.Build"/> raises nothing else under this type, so a defect of
/// the code keeps its own exception and its stack. It derives from
/// <see cref="InvalidOperationException"/>, which the verbs that already answered a refused setting
/// with exit 1 — <c>email</c>, <c>rag</c>, <c>--list-tools</c>, <c>mcp serve</c> — catch.
/// </para>
/// </summary>
public sealed class RunnerSettingsException : InvalidOperationException
{
    /// <summary>Creates the exception with a generic reason; prefer the message overload.</summary>
    public RunnerSettingsException()
        : base("A setting of the runner host was refused.")
    {
    }

    /// <summary>Creates the exception with the operator-facing reason.</summary>
    /// <param name="message">What is refused and how to fix it.</param>
    public RunnerSettingsException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the operator-facing reason and the failure it explains.</summary>
    /// <param name="message">What is refused and how to fix it.</param>
    /// <param name="innerException">The failure the refusal comes from.</param>
    public RunnerSettingsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
