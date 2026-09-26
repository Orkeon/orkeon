using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Accounts;

/// <summary>The declared accounts, resolved on first use.</summary>
internal interface IEmailAccountRegistry
{
    /// <summary>Declared account names, sorted.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>The account a call without <c>account</c> uses, or null when that is ambiguous.</summary>
    string? DefaultName { get; }

    /// <summary>
    /// The account named <paramref name="name"/>, or the default one when null. Throws an
    /// <see cref="EmailToolException"/> that says what to fix when there is no such usable account.
    /// </summary>
    ResolvedEmailAccount Resolve(string? name);

    /// <summary>The resolution of a declared account, problems included, without throwing.</summary>
    EmailAccountResolution Inspect(string name);
}

/// <summary>Default <see cref="IEmailAccountRegistry"/> over the bound <c>Orkeon:Tools:Email</c> section.</summary>
internal sealed class EmailAccountRegistry : IEmailAccountRegistry
{
    private const string GuideHint = "see the e-mail guide, docs/guides/email.md";

    private readonly EmailToolsOptions _options;
    private readonly ConcurrentDictionary<string, EmailAccountResolution> _resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the registry over the bound options.</summary>
    public EmailAccountRegistry(IOptions<EmailToolsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names =>
        _options.Accounts.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

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
    public ResolvedEmailAccount Resolve(string? name)
    {
        if (_options.Accounts.Count == 0)
        {
            throw new EmailToolException(
                EmailErrorCode.NotConfigured,
                $"No e-mail account is configured. Declare one under {Constants.EmailDefaults.SectionName}:Accounts ({GuideHint}).");
        }

        var target = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        if (target is null)
        {
            throw new EmailToolException(
                EmailErrorCode.UnknownAccount,
                $"Several e-mail accounts are configured ({string.Join(", ", Names)}): pass `account` to choose one.");
        }

        var resolution = Inspect(target);
        if (resolution.Account is not null)
            return resolution.Account;

        throw new EmailToolException(
            EmailErrorCode.InvalidConfiguration,
            $"The e-mail account '{target}' is misconfigured: {string.Join("; ", resolution.Problems)} ({GuideHint}).");
    }

    /// <inheritdoc />
    public EmailAccountResolution Inspect(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = _options.Accounts.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            throw new EmailToolException(
                EmailErrorCode.UnknownAccount,
                $"Unknown e-mail account '{name}'. Configured: {(Names.Count == 0 ? "none" : string.Join(", ", Names))}.");
        }

        return _resolved.GetOrAdd(key, k => EmailAccountResolver.Resolve(k, _options.Accounts[k]));
    }
}
