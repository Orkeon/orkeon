namespace Orkeon.Analysis.Core.Cache;

// Written after the tree file: TreeSha256 rejects a tree file another writer replaced or left half written.
internal sealed record CacheManifest
{
    public string CacheKey { get; init; } = string.Empty;
    public string IndexId { get; init; } = string.Empty;
    public string TreeSha256 { get; init; } = string.Empty;
    public Dictionary<string, string> Files { get; init; } = [];
    public Dictionary<string, string> Markers { get; init; } = [];
}
