using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;

namespace Orkeon.Tools.Analysis.DependencyInjection;

public static class RaggableToolsExtensions
{
    // The Tree-sitter tools take the parser pool, an internal of Orkeon.Analysis kept out of the public
    // surface (Tree-sitter types stay off the 1.x promise): they are built here, not by reflection.
    public static IServiceCollection AddRaggableTreeTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IBaseTool, AccessCostTool>();
        services.AddTransient<IBaseTool, AffectedTestsTool>();
        services.AddTransient<IBaseTool, BatchAnchorsTool>();
        services.AddTransient<IBaseTool, CodebaseMapTool>();
        services.AddTransient<IBaseTool, CodebaseSearchTool>();
        services.AddTransient<IBaseTool, ComplexityReportTool>();
        services.AddTransient<IBaseTool, DependencyGraphTool>();
        services.AddTransient<IBaseTool, DocumentedInTool>();
        services.AddTransient<IBaseTool, DocumentsOfTool>();
        services.AddTransient<IBaseTool, EdgeListTool>();
        services.AddTransient<IBaseTool>(sp => new EditBatchTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>(), sp.GetService<IBuildValidator>(), sp.GetService<IIndexInvalidation>()));
        services.AddTransient<IBaseTool, FlowTraceTool>();
        services.AddTransient<IBaseTool, ImpactAnalysisTool>();
        services.AddTransient<IBaseTool>(sp => new NodeEnsureTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>(), sp.GetService<IIndexInvalidation>()));
        services.AddTransient<IBaseTool>(sp => new NodeInsertTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>(), sp.GetService<IIndexInvalidation>()));
        services.AddTransient<IBaseTool>(sp => new NodeOutlineTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>()));
        services.AddTransient<IBaseTool>(sp => new NodeReadTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>()));
        services.AddTransient<IBaseTool>(sp => new NodeReplaceTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>(), sp.GetService<IIndexInvalidation>()));
        services.AddTransient<IBaseTool, PackageSummaryTool>();
        services.AddTransient<IBaseTool, StatementQueryTool>();
        services.AddTransient<IBaseTool>(sp => new StructuralRewriteTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>(), sp.GetService<IIndexInvalidation>()));
        services.AddTransient<IBaseTool>(sp => new StructuralSearchTool(sp.GetRequiredService<IFileSystemService>(), sp.GetRequiredService<TreeSitterParserPool>()));
        services.AddTransient<IBaseTool, SubGraphTool>();
        services.AddTransient<IBaseTool, SymbolDetailTool>();
        services.AddTransient<IBaseTool, SymbolSourceTool>();

        services.AddTransient<IBaseTool, IndexCodebaseTool>();
        services.AddTransient<IBaseTool, IncrementalReindexTool>();
        services.AddTransient<IBaseTool, IndexStatusTool>();
        services.AddTransient<IBaseTool, IsPathIndexedTool>();

        return services;
    }
}
