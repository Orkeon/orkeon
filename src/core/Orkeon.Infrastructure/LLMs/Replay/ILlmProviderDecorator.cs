using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.LLMs.Replay;

/// <summary>
/// A provider that wraps the vendor's own without being a layer of the entrance
/// (<see cref="MeteredLlmProvider.Unwrap"/> looks through it, so that a caller asking which vendor it
/// talks to still gets the vendor's provider).
/// </summary>
internal interface ILlmProviderDecorator
{
    /// <summary>The provider under the decorator.</summary>
    ILlmProvider Inner { get; }
}
