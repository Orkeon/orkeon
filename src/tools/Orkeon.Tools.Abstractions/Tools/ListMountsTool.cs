using System.Text.Json.Serialization;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Abstractions.Tools;

/// <summary>Request for the list_mounts tool (no parameters required).</summary>
public sealed record ListMountsRequest;

/// <summary>Response containing the available mounts.</summary>
public sealed record ListMountsResponse
{
    /// <summary>Whether the operation succeeded.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>The available mount points.</summary>
    [JsonPropertyName("mounts")]
    public IReadOnlyList<MountInfoDto> Mounts { get; init; } = [];
}

/// <summary>DTO representing a single mount point with its virtual path and access rights.</summary>
public sealed record MountInfoDto
{
    /// <summary>The virtual path of the mount.</summary>
    [JsonPropertyName("virtual_path")]
    public string Path { get; init; } = "";

    /// <summary>The access rights for this mount.</summary>
    [JsonPropertyName("access")]
    public string Rights { get; init; } = "";

    /// <summary>Sub-path overrides within the mount.</summary>
    [JsonPropertyName("overrides")]
    public IReadOnlyList<OverrideDto> Overrides { get; init; } = [];
}

/// <summary>DTO representing a sub-path override within a mount.</summary>
public sealed record OverrideDto
{
    /// <summary>The sub-path of the override.</summary>
    [JsonPropertyName("virtual_path")]
    public string Path { get; init; } = "";

    /// <summary>The access rights for this override.</summary>
    [JsonPropertyName("access")]
    public string Rights { get; init; } = "";
}

/// <summary>
/// Tool that lists all available virtual file system mounts with their paths and access rights.
/// This tool is intended for LLM agents to discover which file system areas are accessible.
/// </summary>
public class ListMountsTool : ToolBase<ListMountsRequest, ListMountsResponse>
{
    private readonly IFileSystemService _fileSystemService;

    /// <inheritdoc />
    public override string Name => "list_mounts";

    /// <inheritdoc />
    public override string Description =>
        "Lists all available file system mounts with their virtual paths and access rights.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    /// <inheritdoc />
    public override string Category => "File Operations";

    /// <summary>
    /// Initializes a new instance of <see cref="ListMountsTool"/>.
    /// </summary>
    /// <param name="fileSystemService">The virtual file system service.</param>
    /// <param name="logger">Optional logger.</param>
    public ListMountsTool(IFileSystemService fileSystemService, ILogger? logger = null) : base(logger)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    /// <inheritdoc />
    protected override Task<ListMountsResponse> ExecuteTypedAsync(
        ListMountsRequest request, CancellationToken cancellationToken)
    {
        var mountDtos = _fileSystemService.GetAvailableMounts().Select(m => new MountInfoDto
        {
            Path = m.VirtualPath,
            Rights = FormatRights(m.DefaultRights),
            Overrides = m.Overrides.Select(o => new OverrideDto
            {
                Path = o.RelativePath,
                Rights = FormatRights(o.Rights)
            }).ToList()
        }).ToList();

        return Task.FromResult(new ListMountsResponse { Success = true, Mounts = mountDtos });
    }

    /// <summary>
    /// The grammar's own token for the three rights a mount string can carry (one source of
    /// truth with <see cref="FileSystemMount.FormatRights"/>); any other combination — a mount
    /// registered in code with bare <c>Write</c>, say — is spelled by the enum, never hidden.
    /// </summary>
    private static string FormatRights(FileAccessRights rights) =>
        rights is FileAccessRights.ReadOnly or FileAccessRights.ReadWrite or FileAccessRights.ReadWriteNoDelete
            ? FileSystemMount.FormatRights(rights)
            : rights.ToString();
}
