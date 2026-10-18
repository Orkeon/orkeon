using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Orkeon.Tools.Drive.Configuration;

namespace Orkeon.Tools.Drive.Accounts;

/// <summary>The declared accounts, resolved on first use.</summary>
internal interface IDriveAccountRegistry
{
    /// <summary>Declared account names, sorted.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>The account a call without <c>account</c> uses, or null when that is ambiguous.</summary>
    string? DefaultName { get; }

    /// <summary>
    /// The account named <paramref name="name"/>, or the default one when null. Throws an
    /// <see cref="DriveToolException"/> that says what to fix when there is no such usable account.
    /// </summary>
    ResolvedDriveAccount Resolve(string? name);

    /// <summary>The resolution of a declared account, problems included, without throwing.</summary>
    DriveAccountResolution Inspect(string name);
}

/// <summary>Default <see cref="IDriveAccountRegistry"/> over the bound <c>Orkeon:Tools:Drive</c> section.</summary>
internal sealed class DriveAccountRegistry : IDriveAccountRegistry
{
    private const string GuideHint = "see the drive guide, " + Constants.DriveDefaults.GuidePath;

    private readonly DriveToolsOptions _options;
    private readonly ConcurrentDictionary<string, DriveAccountResolution> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IReadOnlyList<string>> _names;

    /// <summary>Creates the registry over the bound options.</summary>
    public DriveAccountRegistry(IOptions<DriveToolsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        // The bound options are a snapshot: the names are sorted once, like the resolutions are cached.
        _names = new(() => _options.Accounts.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names => _names.Value;

    /// <inheritdoc />
    public string? DefaultName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_options.DefaultAccount))
                return _options.DefaultAccount.Trim();
            return _options.Accounts.Count == 1 ? _options.Accounts.Keys.First() : null;
        }
    }

    /// <inheritdoc />
    public ResolvedDriveAccount Resolve(string? name)
    {
        if (_options.Accounts.Count == 0)
        {
            throw new DriveToolException(
                DriveErrorCode.NotConfigured,
                $"No drive account is configured. Declare one under {Constants.DriveDefaults.SectionName}:Accounts ({GuideHint}).");
        }

        var target = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        if (target is null)
        {
            throw new DriveToolException(
                DriveErrorCode.UnknownAccount,
                $"Several drive accounts are configured ({string.Join(", ", Names)}): pass `account` to choose one.");
        }

        var resolution = Inspect(target);
        if (resolution.Account is not null)
            return resolution.Account;

        throw new DriveToolException(
            DriveErrorCode.InvalidConfiguration,
            $"The drive account '{target}' is misconfigured: {string.Join("; ", resolution.Problems)} ({GuideHint}).");
    }

    /// <inheritdoc />
    public DriveAccountResolution Inspect(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = _options.Accounts.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            throw new DriveToolException(
                DriveErrorCode.UnknownAccount,
                $"Unknown drive account '{name}'. Configured: {(Names.Count == 0 ? "none" : string.Join(", ", Names))}.");
        }

        return _resolved.GetOrAdd(key, k => Unreadable(k) is { Count: > 0 } problems
            ? new DriveAccountResolution(null, problems)
            : DriveAccountResolver.Resolve(k, _options.Accounts[k]));
    }

    /// <summary>The settings of <paramref name="account"/>, and of the section, that could not even be read.</summary>
    private List<string> Unreadable(string account)
    {
        var problems = new List<string>(_options.SectionProblems);
        if (_options.AccountProblems.TryGetValue(account, out var own))
            problems.AddRange(own);
        return problems;
    }
}
