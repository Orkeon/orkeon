using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Rag.Abstractions;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.FileSystem;

/// <summary>Request parameters for the glob tool.</summary>
public sealed record GlobRequest
{
    /// <summary>Wildcard pattern, relative to <see cref="Path"/>.</summary>
    [JsonPropertyName("pattern")]
    [FieldSchema(Description = "Wildcard pattern relative to path; an absolute pattern is refused", Example = "**/*.cs")]
    public string Pattern { get; init; } = "";

    /// <summary>Root directory (virtual path).</summary>
    [JsonPropertyName("path")]
    [FieldSchema(Description = "Root directory (virtual path): an existing mounted directory", Example = "/workspace/src")]
    public string Path { get; init; } = "";

    /// <summary>Maximum number of paths returned.</summary>
    [JsonPropertyName("max_results")]
    [FieldSchema(Description = "Maximum number of paths returned (default: 200)", IsRequired = false, Example = 200)]
    public int MaxResults { get; init; } = 200;
}

/// <summary>Response of the glob tool.</summary>
public sealed record GlobResponse : IBoundedToolResponse
{
    /// <summary>Matching virtual paths.</summary>
    [JsonPropertyName("paths")]
    [ReturnSchema(Description = "Matching virtual paths, sorted")]
    public ImmutableList<string> Paths { get; init; } = [];

    /// <summary>True when matches were left out.</summary>
    [JsonPropertyName("truncated")]
    [ReturnSchema(Description = "True when matches were left out")]
    public bool Truncated { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public bool AlreadyBounded { get; init; }
}

/// <summary>Expands a wildcard pattern under a directory.</summary>
public sealed class GlobTool : FileToolBase<GlobRequest, GlobResponse>
{
    /// <inheritdoc />
    public override string Name => "glob";

    /// <inheritdoc />
    public override string Description => "Expand a wildcard pattern (relative to a mounted directory) and list the matching file paths, sorted and capped.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    /// <summary>Initializes a new instance of <see cref="GlobTool"/>.</summary>
    public GlobTool(IFileSystemService fileSystemService, IPathValidator pathValidator, ILogger<GlobTool>? logger = null)
        : base(fileSystemService, pathValidator, logger) { }

    /// <summary>Total-characters bound of the listed paths.</summary>
    private const int MaxTotalChars = 16_000;

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(GlobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Pattern))
            return "Pattern cannot be empty";
        if (request.Pattern.StartsWith('/') || request.Pattern.StartsWith('\\'))
            return "Pattern must be relative to path, not absolute";
        if (request.Pattern.Split(['/', '\\']).Contains(".."))
            return "Pattern cannot contain a '..' segment";
        if (request.MaxResults <= 0)
            return "max_results must be greater than 0";
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        var validation = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        return validation.IsAllowed ? null : validation.DenialReason ?? "Invalid or unsafe directory path";
    }

    /// <inheritdoc />
    protected override async Task<GlobResponse> ExecuteTypedAsync(GlobRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A relative root resolves against the working directory, as ValidateTypedRequest judged it.
        request = request with { Path = AbsolutizePath(request.Path) };
        var root = request.Path.TrimEnd('/');

        if (!await _fileSystemService.ExistsAsync(request.Path, cancellationToken).ConfigureAwait(false)
            || await _fileSystemService.GetEntryKindAsync(request.Path, cancellationToken).ConfigureAwait(false) != VirtualEntryKind.Directory)
            throw new DirectoryNotFoundException($"Directory not found: {request.Path}");

        var location = $"{root}/{request.Pattern}";
        IEnumerable<string> matches;
        if (SourceGlobExpander.HasWildcard(request.Pattern))
        {
            var descriptors = await SourceGlobExpander.ExpandAsync(_fileSystemService, [location], cancellationToken).ConfigureAwait(false);
            matches = descriptors.Select(d => d.Location);
        }
        else
        {
            matches = (await _fileSystemService.TryGetEntryAsync(location, cancellationToken).ConfigureAwait(false))?.Kind == VirtualEntryKind.File ? [location] : [];
        }

        var paths = ImmutableList.CreateBuilder<string>();
        var chars = 0;
        var truncated = false;
        var bounded = false;
        foreach (var match in matches)
        {
            if (paths.Count >= request.MaxResults)
            {
                truncated = true;
                break;
            }
            if (chars + match.Length > MaxTotalChars)
            {
                truncated = bounded = true;
                break;
            }
            chars += match.Length;
            paths.Add(match);
        }

        return new GlobResponse { Paths = paths.ToImmutable(), Truncated = truncated, AlreadyBounded = bounded };
    }
}
