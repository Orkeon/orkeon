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

    /// <summary>
    /// STUDIO-49: a section's <c>ApiKeyEnvVar</c> names an environment variable set nowhere the
    /// runtime reads, and no key masks it, so every call on that profile answers that an API key
    /// is required. A composite format whose <c>{0}</c> is the reference's configuration path —
    /// never the name it holds, which may be a key pasted in the wrong field. The runner host, the
    /// REPL and <c>orkeon doctor</c> say it.
    /// </summary>
    public const string LlmApiKeyReferenceUnresolved =
        "{0} names an environment variable that is not set: calls on that LLM profile answer that an API key " +
        "is required. Set the variable — in Orkeon Studio, remember the setting's key again — or remove the reference.";
}
