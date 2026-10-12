namespace Orkeon.Infrastructure.LLMs.CallMode;

/// <summary>How the last call reached the model on the wire.</summary>
internal enum LlmWireMode
{
    /// <summary>Streamed.</summary>
    Stream,

    /// <summary>Buffered: Buffer mode, or a provider with no native stream.</summary>
    Buffer,

    /// <summary>Buffered under Auto because the provider refused streaming, or declares none (BR-06).</summary>
    BufferFallback,
}
