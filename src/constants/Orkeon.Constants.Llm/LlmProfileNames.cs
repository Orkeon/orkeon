namespace Orkeon.Constants.Llm;

/// <summary>
/// The names of host LLM profiles (<c>Llm:Profiles:&lt;name&gt;</c>, GAP-17) that more than one
/// project must spell identically.
/// <para>
/// The runtime reserves <see cref="Default"/> — a host that defines <c>Llm:Profiles:default</c>
/// refuses to start — and Orkeon Studio, which writes that section from its model settings
/// (STUDIO-48) and cannot reference the runtime, must refuse the same name before it writes it.
/// </para>
/// </summary>
public static class LlmProfileNames
{
    /// <summary>
    /// The name of the host's default profile: the <c>Llm</c> section itself. No entry of
    /// <c>Llm:Profiles</c> may take it, and a crew may always name it, to bring an agent or a
    /// task back to the default under a crew-level profile. Compared case-insensitively, as the
    /// configuration binder compares keys.
    /// </summary>
    public const string Default = "default";
}
