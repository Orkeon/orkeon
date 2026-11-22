using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// An LLM provider shipped outside <c>Orkeon.Infrastructure</c> and chosen by name: a section written
/// <c>Provider: &lt;key&gt;</c> is built by the module registered with that <see cref="Key"/>, never inferred
/// from <c>BaseUrl</c> or the model's name. The factory meters, limits, records and observes what the
/// module builds exactly like a built-in provider; a key no module answers refuses the call.
/// </summary>
public interface ILlmProviderModule
{
    /// <summary>The value of <c>Llm:Provider</c> this module answers, lowercase (for example <c>agent-cli</c>).</summary>
    string Key { get; }

    /// <summary>Builds the provider for one section's configuration.</summary>
    /// <param name="config">The configuration read from the section that named <see cref="Key"/>.</param>
    /// <param name="context">The host services the provider may use.</param>
    /// <returns>The provider, not yet metered: the factory wraps it.</returns>
    ILlmProvider Create(LlmConfig config, LlmProviderModuleContext context);
}

/// <summary>The host services an <see cref="ILlmProviderModule"/> builds its provider with.</summary>
public sealed class LlmProviderModuleContext
{
    /// <summary>The host's logger factory.</summary>
    public required ILoggerFactory LoggerFactory { get; init; }

    /// <summary>The virtual file system the factory was given.</summary>
    public required IFileSystemService FileSystem { get; init; }
}
