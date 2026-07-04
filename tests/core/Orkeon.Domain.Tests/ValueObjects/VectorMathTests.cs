using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class VectorMathTests
{
    [Fact]
    public void ShouldReturnOne_WhenUsingCosineSimilarityIdenticalVectors()
    {
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { 1f, 2f, 3f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(1f, result, precision: 5);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingCosineSimilarityOrthogonalVectors()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0f, result, precision: 5);
    }

    [Fact]
    public void ShouldReturnNegativeOne_WhenUsingCosineSimilarityOppositeVectors()
    {
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { -1f, 0f, 0f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(-1f, result, precision: 5);
    }

    [Fact]
    public void ShouldThrowArgument_WhenUsingCosineSimilarityDimensionMismatch()
    {
        var a = new float[] { 1f, 2f };
        var b = new float[] { 1f, 2f, 3f };

        var ex = Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(a, b));
        Assert.Contains("mismatch", ex.Message);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingCosineSimilarityWithZeroVector()
    {
        var a = new float[] { 0f, 0f, 0f };
        var b = new float[] { 1f, 2f, 3f };

        var result = VectorMath.CosineSimilarity(a, b);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void ShouldReturnCorrect_WhenUsingDotProductKnownValues()
    {
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { 4f, 5f, 6f };

        // 1*4 + 2*5 + 3*6 = 4 + 10 + 18 = 32
        var result = VectorMath.DotProduct(a, b);

        Assert.Equal(32f, result, precision: 5);
    }

    [Fact]
    public void ShouldThrowArgument_WhenUsingDotProductDimensionMismatch()
    {
        var a = new float[] { 1f };
        var b = new float[] { 1f, 2f };

        var ex = Assert.Throws<ArgumentException>(() => VectorMath.DotProduct(a, b));
        Assert.Contains("mismatch", ex.Message);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingEuclideanDistanceWithSamePoint()
    {
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { 1f, 2f, 3f };

        var result = VectorMath.EuclideanDistance(a, b);

        Assert.Equal(0f, result, precision: 5);
    }

    [Fact]
    public void ShouldReturnCorrect_WhenUsingEuclideanDistanceKnownValues()
    {
        var a = new float[] { 0f, 0f };
        var b = new float[] { 3f, 4f };

        // sqrt(9 + 16) = sqrt(25) = 5
        var result = VectorMath.EuclideanDistance(a, b);

        Assert.Equal(5f, result, precision: 5);
    }

    [Fact]
    public void ShouldThrowArgument_WhenUsingEuclideanDistanceDimensionMismatch()
    {
        var a = new float[] { 1f };
        var b = new float[] { 1f, 2f };

        Assert.Throws<ArgumentException>(() => VectorMath.EuclideanDistance(a, b));
    }

    [Fact]
    public void ShouldHasUnitLength_WhenNormalizingVector()
    {
        var vector = new float[] { 3f, 4f };

        var normalized = VectorMath.Normalize(vector);

        var length = MathF.Sqrt(normalized.Sum(v => v * v));
        Assert.Equal(1f, length, precision: 5);
        Assert.Equal(0.6f, normalized[0], precision: 5);
        Assert.Equal(0.8f, normalized[1], precision: 5);
    }

    [Fact]
    public void ShouldReturnZeroVector_WhenNormalizingWithZeroVector()
    {
        var vector = new float[] { 0f, 0f, 0f };

        var normalized = VectorMath.Normalize(vector);

        Assert.All(normalized, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void ShouldDispatchCorrectly_WhenUsingSimilarityMetricCosine()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 1f, 0f };

        var result = SimilarityMetric.Cosine.Compute(a, b);

        Assert.Equal(1f, result, precision: 5);
    }

    [Fact]
    public void ShouldDispatchCorrectly_WhenUsingSimilarityMetricDotProduct()
    {
        var a = new float[] { 2f, 3f };
        var b = new float[] { 4f, 5f };

        // 2*4 + 3*5 = 23
        var result = SimilarityMetric.DotProduct.Compute(a, b);

        Assert.Equal(23f, result, precision: 5);
    }

    [Fact]
    public void ShouldDispatchCorrectly_WhenUsingSimilarityMetricEuclidean()
    {
        var a = new float[] { 0f, 0f };
        var b = new float[] { 3f, 4f };

        // 1 / (1 + 5) = 1/6
        var result = SimilarityMetric.Euclidean.Compute(a, b);

        Assert.Equal(1f / 6f, result, precision: 5);
    }
}
