using System.Data.Common;

namespace Orkeon.Application.Abstractions.Data;

/// <summary>
/// Factory for obtaining ADO.NET database provider instances by name.
/// </summary>
public interface IDatabaseProviderFactory
{
    /// <summary>
    /// Gets the <see cref="DbProviderFactory"/> for the specified provider name.
    /// </summary>
    /// <param name="providerName">The provider name (e.g. "Microsoft.Data.SqlClient", "Npgsql").</param>
    /// <returns>The corresponding <see cref="DbProviderFactory"/>.</returns>
    DbProviderFactory GetProvider(string providerName);

    /// <summary>
    /// Checks whether the specified provider name is supported.
    /// </summary>
    /// <param name="providerName">The provider name to check.</param>
    /// <returns><c>true</c> if the provider is supported; otherwise <c>false</c>.</returns>
    bool IsSupported(string providerName);

    /// <summary>
    /// Gets the list of supported provider names.
    /// </summary>
    IReadOnlyList<string> SupportedProviders { get; }
}
