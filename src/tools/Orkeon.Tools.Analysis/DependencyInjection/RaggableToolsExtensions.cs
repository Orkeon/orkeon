using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;

namespace Orkeon.Tools.Analysis.DependencyInjection;

public static class RaggableToolsExtensions
{
    public static IServiceCollection AddRaggableTreeTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IBaseTool, CodebaseMapTool>();
        services.AddTransient<IBaseTool, CodebaseSearchTool>();
        services.AddTransient<IBaseTool, ComplexityReportTool>();
        services.AddTransient<IBaseTool, DependencyGraphTool>();
        services.AddTransient<IBaseTool, FlowTraceTool>();
        services.AddTransient<IBaseTool, ImpactAnalysisTool>();
        services.AddTransient<IBaseTool, PackageSummaryTool>();
        services.AddTransient<IBaseTool, StatementQueryTool>();
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
