using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Examples.RaggableTree.CrewYaml;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var yamlPath = args.Length > 0 ? args[0] : "crew.yaml";
        var physicalRoot = Path.GetFullPath(args.Length > 1 ? args[1] : Directory.GetCurrentDirectory());
        const string virtualRoot = "/src";

        if (!File.Exists(yamlPath))
        {
            await Console.Error.WriteLineAsync($"YAML not found: {yamlPath}");
            return 1;
        }

        Console.WriteLine($"Loading crew config from {yamlPath}");
        Console.WriteLine($"Indexing {physicalRoot} (mounted at {virtualRoot})");

        var mount = new FileSystemMount(physicalRoot, virtualRoot, FileAccessRights.ReadOnly);
        using var registry = new FileSystemRegistry([mount]);
        IFileSystemService fs = new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance);

        var builder = new RaggableTreeBuilder(
            [new TypeScriptAdapter(), new CSharpAdapter()],
            fs);

        var result = await builder.BuildAsync(
            virtualRoot,
            new IndexCodebaseRequest { RootPath = virtualRoot },
            CancellationToken.None);

        Console.WriteLine($"Indexed {result.Tree.Nodes.Count} nodes, {result.Tree.Edges.Count} edges.");
        Console.WriteLine("The YAML file declares a 3-agent crew (navigator, analyst, flow_tracer)");
        Console.WriteLine("that consumes the produced index via the RaggableTree tools registered by");
        Console.WriteLine("AddRaggableTreeTools. Wire the crew runner of your choice to execute it.");

        return 0;
    }

    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
            PathValidationResult.Allowed(requestedPath);
    }
}
