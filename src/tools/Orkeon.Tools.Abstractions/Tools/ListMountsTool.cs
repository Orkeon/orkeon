using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Abstractions.Base;
using Microsoft.Extensions.Logging;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ProtocolToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;

namespace Orkeon.Tools.Abstractions.Tools;

/// <summary>Request for the list_mounts tool (no parameters required).</summary>
public sealed record ListMountsRequest;

/// <summary>Response containing the available mounts.</summary>
public sealed record ListMountsResponse
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The available mount points.</summary>
    public IReadOnlyList<MountInfoDto> Mounts { get; init; } = [];
}

/// <summary>DTO representing a single mount point with its virtual path and access rights.</summary>
public sealed record MountInfoDto
{
    /// <summary>The virtual path of the mount.</summary>
    public string Path { get; init; } = "";

    /// <summary>The access rights for this mount.</summary>
    public string Rights { get; init; } = "";

    /// <summary>Sub-path overrides within the mount.</summary>
    public IReadOnlyList<OverrideDto> Overrides { get; init; } = [];
}

/// <summary>DTO representing a sub-path override within a mount.</summary>
public sealed record OverrideDto
{
    /// <summary>The sub-path of the override.</summary>
    public string Path { get; init; } = "";

    /// <summary>The access rights for this override.</summary>
    public string Rights { get; init; } = "";
}

/// <summary>
/// Tool that lists all available virtual file system mounts with their paths and access rights.
/// This tool is intended for LLM agents to discover which file system areas are accessible.
/// </summary>
public class ListMountsTool : ToolBase
{
    private readonly IFileSystemService _fileSystemService;

    /// <inheritdoc />
    public override string Name => "list_mounts";

    /// <inheritdoc />
    public override string Description =>
        "Lists all available file system mounts with their virtual paths and access rights.";

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
    protected override Task<ProtocolToolCallResponse> ExecuteCoreAsync(
        ProtocolToolCallRequest request, CancellationToken cancellationToken)
    {
        var mounts = _fileSystemService.GetAvailableMounts();
        var mountDtos = mounts.Select(m => new MountInfoDto
        {
            Path = m.VirtualPath,
            Rights = FormatRights(m.DefaultRights),
            Overrides = m.Overrides.Select(o => new OverrideDto
            {
                Path = o.RelativePath,
                Rights = FormatRights(o.Rights)
            }).ToList()
        }).ToList();

        var resultDict = new Dictionary<string, object>
        {
            ["success"] = true,
            ["mounts"] = mountDtos
        };

        return Task.FromResult(new ProtocolToolCallResponse(
            Success: true,
            Result: resultDict,
            Error: null));
    }

    private static string FormatRights(FileAccessRights rights)
    {
        return rights switch
        {
            FileAccessRights.ReadOnly => "ro",
            FileAccessRights.ReadWrite => "rw",
            FileAccessRights.ReadWriteNoDelete => "rwnd",
            _ => rights.ToString()
        };
    }
}
