namespace Orkeon.Infrastructure.Memory.Pinecone;

/// <summary>
/// Configuration options for the Pinecone memory provider, bound from the host's
/// <c>Orkeon:Pinecone</c> section.
/// </summary>
public class PineconeOptions
{
    /// <summary>The configuration section bound to these options.</summary>
    public const string SectionName = "Orkeon:Pinecone";

    /// <summary>Gets or sets the Pinecone API key.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Gets or sets the name of the Pinecone index.</summary>
    public string IndexName { get; set; } = "orkeon-memories";

    /// <summary>
    /// Gets or sets the index host, as Pinecone reports it
    /// (e.g. <c>orkeon-memories-abc1234.svc.aped-4627-b74a.pinecone.io</c>; a scheme is optional).
    /// When empty, the provider asks the control plane once, on first use
    /// (<c>GET https://api.pinecone.io/indexes/{IndexName}</c>), and uses the <c>host</c> it returns.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>Gets or sets the namespace within the index.</summary>
    public string Namespace { get; set; } = "default";
}
