namespace Orkeon.Infrastructure.Memory.Pinecone;

/// <summary>
/// Configuration options for the Pinecone memory provider.
/// </summary>
public class PineconeOptions
{
    /// <summary>Gets or sets the Pinecone API key.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Gets or sets the Pinecone environment (e.g., "us-east1-gcp").</summary>
    public string Environment { get; set; } = "";

    /// <summary>Gets or sets the name of the Pinecone index.</summary>
    public string IndexName { get; set; } = "orkeon-memories";

    /// <summary>Gets or sets the namespace within the index.</summary>
    public string Namespace { get; set; } = "default";
}
