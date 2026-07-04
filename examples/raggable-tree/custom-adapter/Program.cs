using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Examples.RaggableTree.CustomAdapter;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var physicalRoot = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
        const string virtualRoot = "/src";

        var mount = new FileSystemMount(physicalRoot, virtualRoot, FileAccessRights.ReadOnly);
        var registry = new FileSystemRegistry([mount]);
        IFileSystemService fs = new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance);

        var builder = new RaggableTreeBuilder(
            [new TypeScriptAdapter(), new JavaAdapter()],
            fs);

        var request = new IndexCodebaseRequest { RootPath = virtualRoot };
        var result = await builder.BuildAsync(virtualRoot, request, CancellationToken.None);

        Console.WriteLine($"Indexed {result.Tree.Nodes.Count} nodes across TypeScript + Java.");
        Console.WriteLine("JavaAdapter provides the 7 Tree-sitter queries and node/statement mappings.");
        Console.WriteLine("Register it via DI to see it picked up by AddRaggableTree() automatically.");
        return 0;
    }

    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
            PathValidationResult.Allowed(requestedPath);
    }
}
