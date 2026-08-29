namespace Orkeon.Constants.Configuration;

/// <summary>
/// Wording more than one component has to say identically.
/// <para>
/// A message lives here only when two components must word it the same way, and only for the
/// reason that makes it matter: a UI that warns differently from the runtime is worse than one
/// that does not warn at all. Everything else stays where it is said — Studio's own localised
/// text belongs to <c>IStudioStrings</c>, and a message a single component emits belongs to
/// that component.
/// </para>
/// </summary>
public static class OperatorMessages
{
    /// <summary>
    /// WIN-01: no <c>Llm</c> section, so the runtime falls back to the echo provider and answers
    /// nothing useful. The runner prints it at startup and Orkeon Studio reproduces it in its
    /// pre-save validation, where the operator can still act on it.
    /// </summary>
    public const string LlmNotConfigured =
        "No `Llm` section configured — falling back to the echo provider (`<undefined-llm>`). " +
        "Run `orkeon init` to create a configuration, or set `ORKEON_Llm__BaseUrl` / `ORKEON_Llm__Model`.";
}
