using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core;

internal static class TreeAssembly
{
    public static RaggableNode BuildMonorepoNode(string virtualRoot)
    {
        var normalized = NormalizeVirtualPath(virtualRoot);
        var name = TrailingSegment(normalized);
        if (string.IsNullOrEmpty(name)) name = normalized;
        return new RaggableNode
        {
            Id = $"monorepo::{normalized}",
            Kind = UniversalNodeKind.Monorepo,
            Name = name,
            VirtualFilePath = normalized,
            Range = new NodeRange(0, 0, 0, 0, 0),
            Level = NodeLevel.L0_Monorepo,
            Language = "multi",
            SourceSnippet = string.Empty,
            Sha256 = string.Empty,
            Fqn = normalized,
        };
    }

    public static List<RaggableNode> BuildPackageNodes(
        RaggableNode monorepo,
        ImmutableArray<DetectedPackage> packages,
        string virtualRoot)
    {
        var root = NormalizeVirtualPath(virtualRoot);
        var nodes = new List<RaggableNode>();
        foreach (var pkg in packages)
        {
            var virtualPath = string.IsNullOrEmpty(pkg.RelativePath)
                ? root
                : $"{root}/{pkg.RelativePath.Replace('\\', '/').TrimStart('/')}";
            var node = new RaggableNode
            {
                Id = $"pkg::{virtualPath}",
                Kind = UniversalNodeKind.Package,
                Name = pkg.Name,
                VirtualFilePath = virtualPath,
                Range = new NodeRange(0, 0, 0, 0, 0),
                Level = NodeLevel.L1_Package,
                Language = "multi",
                SourceSnippet = string.Empty,
                Sha256 = string.Empty,
                Fqn = $"{monorepo.Fqn}::{pkg.Name}",
            };
            node.ParentId = monorepo.Id;
            node.TagsMutable["marker"] = pkg.MarkerFile;
            monorepo.ChildrenIdsMutable.Add(node.Id);
            nodes.Add(node);
        }
        return nodes;
    }

    public static void LinkToPackage(
        IReadOnlyList<RaggableNode> fileNodes,
        List<RaggableNode> packages,
        RaggableNode monorepo)
    {
        var module = fileNodes.FirstOrDefault(n => n.Level == NodeLevel.L2_Module);
        if (module is null) return;

        RaggableNode? chosen = null;
        var bestMatch = -1;
        foreach (var pkg in packages)
        {
            if (string.IsNullOrEmpty(pkg.VirtualFilePath)) continue;
            var prefix = pkg.VirtualFilePath.TrimEnd('/');
            if (!module.VirtualFilePath.Equals(prefix, StringComparison.Ordinal)
                && !module.VirtualFilePath.StartsWith(prefix + "/", StringComparison.Ordinal))
                continue;
            if (prefix.Length > bestMatch)
            {
                bestMatch = prefix.Length;
                chosen = pkg;
            }
        }

        var parent = chosen ?? monorepo;
        module.ParentId = parent.Id;
        if (!parent.ChildrenIds.Contains(module.Id)) parent.ChildrenIdsMutable.Add(module.Id);
    }

    private static string NormalizeVirtualPath(string path)
    {
        var trimmed = path.Replace('\\', '/').TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" : trimmed;
    }

    private static string TrailingSegment(string virtualPath)
    {
        var slash = virtualPath.LastIndexOf('/');
        return slash < 0 ? virtualPath : virtualPath[(slash + 1)..];
    }

    public static string ComputeIndexId(ImmutableArray<DiscoveredFile> files)
    {
        var sb = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            sb.Append(file.RelativePath).Append(':').Append(file.Sha256).Append('\n');
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
