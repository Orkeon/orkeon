using System.Diagnostics.CodeAnalysis;

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

    /// <summary>A <c>BaseUrl</c> no run serves: the refusal, naming the key and the address.</summary>
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "Returns an operator sentence, not an address.")]
    public static string LlmBaseUrlRefused(string settingPath, string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return $"{settingPath} is '{Shown(address)}', which no run serves: write an absolute http:// or https:// address, " +
        "with its scheme (for example http://localhost:11434), and no vendor path on another vendor's host.";
    }

    /// <summary>A vendor's address under its <c>/anthropic</c> path: the Anthropic dialect, not the one its provider builds.</summary>
    public static string LlmVendorAnthropicPathRefused(string settingPath, string address, string vendor)
    {
        ArgumentNullException.ThrowIfNull(address);
        var shown = Shown(address);
        var authority = shown.IndexOf('/', shown.IndexOf("//", StringComparison.Ordinal) + 2);
        var standard = authority < 0 ? shown : shown[..authority];
        return $"{settingPath} is '{shown}', a {vendor} address under an /anthropic path, which speaks the " +
        "Anthropic Messages API dialect, not the OpenAI dialect this provider builds. " +
        $"Use the standard endpoint ({standard}): it serves the same models.";
    }

    /// <summary>A <c>custom</c> address with no path: read as an OpenAI-compatible server at its root.</summary>
    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "Returns an operator sentence, not an address.")]
    public static string LlmBaseUrlSilent(string settingPath, string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return $"{settingPath} is '{Shown(address)}', with no path: it is read as an OpenAI-compatible server answering at its root. " +
        "Add the path of its routes, often /v1; an Ollama server is read on its port 11434.";
    }

    /// <summary>The address as written, without user info, query or fragment: a secret may sit in them.</summary>
    private static string Shown(string address)
    {
        var text = address.Trim();
        var end = text.IndexOfAny(['?', '#']);
        if (end >= 0)
            text = text[..end];
        var authority = text.IndexOf("//", StringComparison.Ordinal) + 2;
        var at = text.LastIndexOf('@');
        var slash = text.IndexOf('/', authority);
        if (at >= authority && (slash < 0 || at < slash))
            text = text[..authority] + text[(at + 1)..];
        return text;
    }
}
