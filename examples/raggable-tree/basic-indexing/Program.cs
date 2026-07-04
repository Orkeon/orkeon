using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Examples.RaggableTree.BasicIndexing;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var physicalRoot = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
        const string virtualRoot = "/src";

        Console.WriteLine($"Indexing {physicalRoot} (mounted at {virtualRoot})...");

        var mount = new FileSystemMount(physicalRoot, virtualRoot, FileAccessRights.ReadOnly);
        var registry = new FileSystemRegistry([mount]);
        IFileSystemService fs = new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance);

        var builder = new RaggableTreeBuilder(
            [
                new TypeScriptAdapter(),
                new CSharpAdapter(),
                new PythonAdapter(),
                new GoAdapter(),
                new RustAdapter(),
            ],
            fs);

        var request = new IndexCodebaseRequest
        {
            RootPath = virtualRoot,
            Exclude = ["node_modules", "dist", ".git", "bin", "obj"],
        };

        var result = await builder.BuildAsync(virtualRoot, request, CancellationToken.None);

        Console.WriteLine($"  IndexId : {result.IndexId}");
        Console.WriteLine($"  Files   : {result.FileCount}");
        Console.WriteLine($"  Nodes   : {result.Tree.Nodes.Count}");
        Console.WriteLine($"  Edges   : {result.Tree.Edges.Count}");

        var symbols = result.Tree.Nodes.Count(n => n.Level == NodeLevel.L3_Symbol);
        var modules = result.Tree.Nodes.Count(n => n.Level == NodeLevel.L2_Module);
        Console.WriteLine($"  Modules : {modules}");
        Console.WriteLine($"  Symbols : {symbols}");

        return 0;
    }

    private sealed class AllowAllPathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
            PathValidationResult.Allowed(requestedPath);
    }
}
