using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Abstractions.Tests;

/// <summary>
/// Contract-shape tests for the RAG-05/C1 query-transform additions:
/// <see cref="QueryTransformKind"/>, the additive (default-implemented)
/// <see cref="IQueryTransformer.Kind"/> member, and the stage options defaults.
/// </summary>
public class QueryTransformerContractTests
{
    [Fact]
    public void Kind_DefaultsToUnion_ForImplementationsThatPredateTheMember()
    {
        // The Kind member is a default interface member so the RAG-05 addition
        // stays ADDITIVE: pre-existing third-party transformers keep compiling
        // and behave as plain union transformers.
        IQueryTransformer transformer = new LegacyTransformer();

        Assert.Equal(QueryTransformKind.Union, transformer.Kind);
    }

    [Fact]
    public void QueryTransformKind_ExposesTheThreeRetrieveSemantics()
    {
        Assert.Equal(
            new[] { QueryTransformKind.Union, QueryTransformKind.Fusion, QueryTransformKind.Replacement },
            Enum.GetValues<QueryTransformKind>());
    }

    [Fact]
    public void QueryTransformOptions_DefaultToNoneWithThreeVariants()
    {
        var options = new RagQueryTransformOptions();

        Assert.Equal("none", options.Mode);
        Assert.Equal(3, options.VariantCount);
    }

    [Fact]
    public void QueryTransformContext_DefaultsToThreeVariants()
    {
        var context = new QueryTransformContext();

        Assert.Equal(3, context.MaxVariants);
        Assert.Null(context.Collection);
        Assert.Empty(context.Extensions);
    }

    private sealed class LegacyTransformer : IQueryTransformer
    {
        public string Name => "legacy";

        public Task<IReadOnlyList<string>> TransformAsync(
            string query,
            QueryTransformContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([query]);
    }
}
