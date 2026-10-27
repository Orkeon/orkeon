using System.Reflection;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase members: this CLR type is exposed to scripts as ctx.services.get("runtime")

/// <summary>
/// The runtime that runs a scripted command, as <c>orkeon doctor</c> prints it:
/// <c>ctx.services.get("runtime")</c> answers <c>{ version, commit, channel }</c>.
/// </summary>
/// <remarks>
/// The host registers its own instance (<c>services.AddSingleton(ScriptRuntimeInfo.FromAssembly(...))</c>),
/// read from its entry assembly and from the install-channel marker of its installation. A host that registers
/// none gets the entry assembly's version and the channel <see cref="Unknown"/>: this library never reads the
/// version of the <c>orkeon</c> executable, which a host need not carry.
/// </remarks>
public sealed class ScriptRuntimeInfo
{
    /// <summary>The value of a version or a channel nothing names.</summary>
    public const string Unknown = "unknown";

    private ScriptRuntimeInfo(string version, string? commit, string channel)
    {
        this.version = version;
        this.commit = commit;
        this.channel = channel;
    }

    /// <summary>The release, build metadata removed (<c>1.0.0-rc.6</c>), as <c>orkeon doctor</c> prints it.</summary>
    public string version { get; }

    /// <summary>
    /// The build metadata SourceLink appends after <c>+</c> (the commit sha); null when the version carries none.
    /// </summary>
    public string? commit { get; }

    /// <summary>
    /// The channel the runtime was installed through (<c>dotnet-tool</c>, <c>deb</c>, <c>tarball</c>, <c>source</c>…),
    /// <see cref="Unknown"/> when nothing names one.
    /// </summary>
    public string channel { get; }

    /// <summary>Splits an informational version (<c>1.0.0-rc.6+&lt;sha&gt;</c>) into the release and the commit.</summary>
    /// <param name="informationalVersion">The informational version; blank reads <see cref="Unknown"/>.</param>
    /// <param name="channel">The install channel; blank reads <see cref="Unknown"/>.</param>
    public static ScriptRuntimeInfo FromInformationalVersion(string? informationalVersion, string? channel)
    {
        var resolvedChannel = string.IsNullOrWhiteSpace(channel) ? Unknown : channel.Trim();
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return new ScriptRuntimeInfo(Unknown, null, resolvedChannel);

        var text = informationalVersion.Trim();
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
            return new ScriptRuntimeInfo(text, null, resolvedChannel);

        var release = text[..plus];
        var metadata = text[(plus + 1)..];
        return new ScriptRuntimeInfo(
            release.Length == 0 ? Unknown : release,
            metadata.Length == 0 ? null : metadata,
            resolvedChannel);
    }

    /// <summary>
    /// Reads the informational version of <paramref name="assembly"/>, else its assembly version, else
    /// <see cref="Unknown"/>.
    /// </summary>
    /// <param name="assembly">The host's entry assembly; null reads <see cref="Unknown"/>.</param>
    /// <param name="channel">The install channel; blank reads <see cref="Unknown"/>.</param>
    public static ScriptRuntimeInfo FromAssembly(Assembly? assembly, string? channel)
    {
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
            informational = assembly?.GetName().Version?.ToString();
        return FromInformationalVersion(informational, channel);
    }
}
