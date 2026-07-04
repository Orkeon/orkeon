using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Tests.DTOs.Common;

public class ApiMetadataTests
{
    // ── ApiMetadataBase.Get<T> ──

    [Fact]
    public void ShouldReturnValue_WhenKeyExistsInMetadata()
    {
        // Arrange
        var metadata = ApiResponseMetadata.CreateBuilder()
            .AddServerVersion("2.0")
            .Build();

        // Act
        var version = metadata.Get<string>("serverVersion");

        // Assert
        Assert.Equal("2.0", version);
    }

    [Fact]
    public void ShouldReturnDefault_WhenKeyDoesNotExistInMetadata()
    {
        // Arrange
        var metadata = ApiResponseMetadata.Empty;

        // Act
        var result = metadata.Get<string>("missing");

        // Assert
        Assert.Null(result);
    }

    // ── ApiMetadataBase.GetRequired<T> ──

    [Fact]
    public void ShouldReturnValue_WhenKeyExistsInGetRequired()
    {
        // Arrange
        var metadata = ApiResponseMetadata.CreateBuilder()
            .AddTraceId("trace-1")
            .Build();

        // Act
        var traceId = metadata.GetRequired<string>("traceId");

        // Assert
        Assert.Equal("trace-1", traceId);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenKeyMissingInGetRequired()
    {
        // Arrange
        var metadata = ApiResponseMetadata.Empty;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => metadata.GetRequired<string>("nonexistent"));
        Assert.Contains("nonexistent", ex.Message);
    }

    // ── Contains / Count / Keys ──

    [Fact]
    public void ShouldReturnTrue_WhenKeyExistsInContains()
    {
        // Arrange
        var metadata = ApiResponseMetadata.CreateBuilder()
            .Add("custom", "val")
            .Build();

        // Act & Assert
        Assert.True(metadata.Contains("custom"));
        Assert.False(metadata.Contains("other"));
    }

    [Fact]
    public void ShouldReturnCorrectCount_WhenMetadataHasEntries()
    {
        // Arrange
        var metadata = ApiResponseMetadata.CreateBuilder()
            .AddServerVersion("1.0")
            .AddTraceId("t-1")
            .Build();

        // Assert
        Assert.Equal(2, metadata.Count);
    }

    [Fact]
    public void ShouldReturnZeroCount_WhenMetadataIsEmpty()
    {
        // Assert
        Assert.Equal(0, ApiResponseMetadata.Empty.Count);
    }

    // ── ToImmutableDictionary ──

    [Fact]
    public void ShouldConvertToImmutableDictionary_WhenCalled()
    {
        // Arrange
        var metadata = ApiResponseMetadata.CreateBuilder()
            .Add("k1", "v1")
            .Add("k2", 42)
            .Build();

        // Act
        var dict = metadata.ToImmutableDictionary();

        // Assert
        Assert.Equal(2, dict.Count);
        Assert.Equal("v1", dict["k1"]);
        Assert.Equal(42, dict["k2"]);
    }

    // ── Builders for specific metadata types ──

    [Fact]
    public void ShouldBuildResponseMetadata_WhenUsingBuilder()
    {
        // Arrange & Act
        var metadata = ApiResponseMetadata.CreateBuilder()
            .AddRequestDuration(TimeSpan.FromMilliseconds(150))
            .AddServerVersion("3.1")
            .AddTraceId("abc")
            .Build();

        // Assert
        Assert.Equal(3, metadata.Count);
        Assert.True(metadata.Contains("requestDuration"));
        Assert.True(metadata.Contains("serverVersion"));
        Assert.True(metadata.Contains("traceId"));
    }

    [Fact]
    public void ShouldBuildErrorDetails_WhenUsingBuilder()
    {
        // Act
        var details = ErrorDetails.CreateBuilder()
            .AddStackTrace("at Foo()")
            .AddInnerError("NRE")
            .AddErrorSource("Service.cs")
            .Build();

        // Assert
        Assert.Equal(3, details.Count);
        Assert.Equal("at Foo()", details.GetRequired<string>("stackTrace"));
        Assert.Equal("NRE", details.GetRequired<string>("innerError"));
        Assert.Equal("Service.cs", details.GetRequired<string>("errorSource"));
    }

    [Fact]
    public void ShouldReturnEmptyErrorDetails_WhenUsingStaticEmpty()
    {
        // Act & Assert
        Assert.Equal(0, ErrorDetails.Empty.Count);
    }

    [Fact]
    public void ShouldBuildEntityMetadata_WhenUsingBuilder()
    {
        // Act
        var meta = EntityMetadata.CreateBuilder()
            .AddCreatedBy("user-1")
            .AddTags(["a", "b"])
            .AddVersion(3)
            .AddCustomField("extra", "value")
            .Build();

        // Assert
        Assert.Equal(4, meta.Count);
        Assert.Equal("user-1", meta.GetRequired<string>("createdBy"));
        Assert.Equal(3, meta.GetRequired<int>("version"));
    }

    [Fact]
    public void ShouldBuildExecutionParameters_WhenUsingBuilder()
    {
        // Act
        var ep = ExecutionParameters.CreateBuilder()
            .AddMaxRetries(5)
            .AddTimeout(TimeSpan.FromSeconds(60))
            .AddPriority("High")
            .Build();

        // Assert
        Assert.Equal(3, ep.Count);
        Assert.True(ep.Contains("maxRetries"));
        Assert.True(ep.Contains("timeout"));
        Assert.True(ep.Contains("priority"));
    }

    [Fact]
    public void ShouldBuildSearchFilters_WhenUsingBuilder()
    {
        // Arrange
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var filters = SearchFilters.CreateBuilder()
            .AddDateRange(start, end)
            .AddStatus("Running")
            .AddTags(["prod"])
            .Build();

        // Assert
        Assert.Equal(4, filters.Count); // dateStart + dateEnd + status + tags
        Assert.True(filters.Contains("dateStart"));
        Assert.True(filters.Contains("dateEnd"));
        Assert.True(filters.Contains("status"));
    }

    [Fact]
    public void ShouldReturnEmptySearchFilters_WhenUsingStaticEmpty()
    {
        // Assert
        Assert.Equal(0, SearchFilters.Empty.Count);
    }

    // ── ApiMetadataValue typed conversion ──

    [Fact]
    public void ShouldReturnDirectType_WhenValueMatchesRequestedType()
    {
        // Arrange
        var val = ApiMetadataValue.From(42);

        // Act
        var result = val.GetValue<int>();

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ShouldConvertType_WhenConvertChangeTypeSucceeds()
    {
        // Arrange
        var val = ApiMetadataValue.From(42);

        // Act — int to double via Convert.ChangeType
        var result = val.GetValue<double>();

        // Assert
        Assert.Equal(42.0, result);
    }

    [Fact]
    public void ShouldThrowInvalidCast_WhenConversionNotPossible()
    {
        // Arrange
        var val = ApiMetadataValue.From("not-a-number");

        // Act & Assert
        Assert.Throws<InvalidCastException>(() => val.GetValue<int>());
    }

    [Fact]
    public void ShouldExposeRawValueAndType_WhenAccessing()
    {
        // Arrange
        var val = ApiMetadataValue.From("hello");

        // Assert
        Assert.Equal("hello", val.RawValue);
        Assert.Equal(typeof(string), val.ValueType);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCreatingFromNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ApiMetadataValue.From(null!));
    }

    // ── TaskContextMetadata builder ──

    [Fact]
    public void ShouldBuildTaskContextMetadata_WhenUsingBuilder()
    {
        // Act
        var ctx = TaskContextMetadata.CreateBuilder()
            .AddInputData("raw input")
            .AddExpectedFormat("json")
            .AddValidationRules(["required"])
            .Add("custom", 99)
            .Build();

        // Assert
        Assert.Equal(4, ctx.Count);
        Assert.Equal("raw input", ctx.GetRequired<string>("inputData"));
    }

    // ── ExecutionContext builder ──

    [Fact]
    public void ShouldBuildExecutionContext_WhenUsingBuilder()
    {
        // Act
        var ctx = Orkeon.Application.Common.DTOs.ExecutionContext.CreateBuilder()
            .AddEnvironment("production")
            .AddUserId("u-1")
            .AddSessionId("s-1")
            .Build();

        // Assert
        Assert.Equal(3, ctx.Count);
        Assert.Equal("production", ctx.GetRequired<string>("environment"));
    }

    // ── ComponentHealthMetadata builder ──

    [Fact]
    public void ShouldBuildComponentHealthMetadata_WhenUsingBuilder()
    {
        // Act
        var meta = ComponentHealthMetadata.CreateBuilder()
            .AddVersion("1.2.3")
            .AddConnectionCount(5)
            .AddLastError("timeout")
            .Build();

        // Assert
        Assert.Equal(3, meta.Count);
        Assert.Equal(5, meta.GetRequired<int>("connectionCount"));
    }
}
