using System.Xml.Linq;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Emits the project-level edges of a full build: <see cref="EdgeKind.Contains"/> from each package or
/// monorepo node to the modules it parents, and <see cref="EdgeKind.ProjectDepends"/> between csproj packages.
/// </summary>
internal static class ProjectGraphBuilder
{
    public static async Task<List<RaggableEdge>> BuildEdgesAsync(
        IReadOnlyList<RaggableNode> nodes,
        IFileSystemService fileSystem,
        CancellationToken ct)
    {
        var edges = BuildContainsEdges(nodes);
        edges.AddRange(await BuildProjectDependsAsync(nodes, fileSystem, null, ct).ConfigureAwait(false));
        return edges;
    }

    /// <summary>The <see cref="EdgeKind.Contains"/> edges from the package or monorepo parent of each node.</summary>
    public static List<RaggableEdge> BuildContainsEdges(IEnumerable<RaggableNode> nodes)
    {
        var edges = new List<RaggableEdge>();
        foreach (var node in nodes)
        {
            if (node.ParentId is null || node.Level > NodeLevel.L2_Module) continue;
            edges.Add(new RaggableEdge($"contains::{node.ParentId}->{node.Id}", node.ParentId, node.Id, EdgeKind.Contains));
        }

        return edges;
    }

    /// <summary>The csproj file a csproj package node was read from.</summary>
    public static string CsprojPath(RaggableNode project) => $"{project.VirtualFilePath}/{project.Name}.csproj";

    /// <summary>
    /// The <see cref="EdgeKind.ProjectDepends"/> edges of the csproj packages in <paramref name="nodes"/>; when
    /// <paramref name="onlyCsprojPaths"/> is given, only the projects whose csproj it holds are read.
    /// </summary>
    public static async Task<List<RaggableEdge>> BuildProjectDependsAsync(
        IReadOnlyList<RaggableNode> nodes,
        IFileSystemService fileSystem,
        IReadOnlySet<string>? onlyCsprojPaths,
        CancellationToken ct)
    {
        var edges = new List<RaggableEdge>();
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var projects = nodes
            .Where(n => n.Kind == UniversalNodeKind.Package && n.Tags.TryGetValue("marker", out var m) && m == "csproj")
            .ToList();
        var byPath = projects.ToDictionary(p => p.VirtualFilePath, StringComparer.Ordinal);
        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();
            var csproj = CsprojPath(project);
            if (onlyCsprojPaths is not null && !onlyCsprojPaths.Contains(csproj)) continue;
            var xml = await fileSystem.TryReadAllTextAsync(csproj, ct).ConfigureAwait(false);
            if (xml is null) continue;
            foreach (var include in ReadProjectReferences(xml))
            {
                var target = ResolveDirectory(project.VirtualFilePath, include);
                if (target is null || !byPath.TryGetValue(target, out var dependency) || dependency.Id == project.Id) continue;
                var id = $"project-depends::{project.Id}->{dependency.Id}";
                if (!edgeIds.Add(id)) continue;
                edges.Add(new RaggableEdge(id, project.Id, dependency.Id, EdgeKind.ProjectDepends));
            }
        }

        return edges;
    }

    private static List<string> ReadProjectReferences(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Descendants()
                .Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>()
                .ToList();
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    private static string? ResolveDirectory(string projectDirectory, string include)
    {
        var segments = projectDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        var parts = include.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts.Take(parts.Length - 1))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (segments.Count == 0) return null;
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(part);
            }
        }

        return "/" + string.Join('/', segments);
    }
}
