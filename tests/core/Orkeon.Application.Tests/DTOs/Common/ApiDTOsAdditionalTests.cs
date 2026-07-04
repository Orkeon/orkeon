using System.Text.Json;
using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Tests.DTOs.Common;

public class ApiDTOsAdditionalTests
{
    [Fact]
    public void ExecutionStatusDto_ShouldInitializeDefaults()
    {
        var dto = new ExecutionStatusDto
        {
            Id = "exec-1",
            CrewId = "crew-1",
            Status = "Running"
        };

        Assert.Equal("exec-1", dto.Id);
        Assert.Equal("crew-1", dto.CrewId);
        Assert.Equal("Running", dto.Status);
        Assert.Equal(0.0, dto.ProgressPercentage);
        Assert.Equal(0, dto.CompletedTasks);
        Assert.Equal(0, dto.TotalTasks);
        Assert.Null(dto.CurrentTask);
        Assert.Null(dto.EstimatedCompletion);
        Assert.Empty(dto.Errors);
        Assert.Null(dto.PerformanceMetrics);
    }

    [Fact]
    public void ExecutionStatusDto_ShouldSerializeWithSnakeCaseNames()
    {
        var dto = new ExecutionStatusDto
        {
            Id = "exec-1",
            CrewId = "crew-1",
            Status = "Completed",
            ProgressPercentage = 100.0,
            CompletedTasks = 3,
            TotalTasks = 3
        };

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("\"crew_id\"", json);
        Assert.Contains("\"progress_percentage\"", json);
        Assert.Contains("\"completed_tasks\"", json);
        Assert.Contains("\"total_tasks\"", json);
    }

    [Fact]
    public void SearchRequestDto_ShouldHaveCorrectDefaults()
    {
        var dto = new SearchRequestDto();

        Assert.Null(dto.Query);
        Assert.Null(dto.SortBy);
        Assert.Equal(1, dto.Page);
        Assert.False(dto.IncludeMetadata);
        Assert.NotNull(dto.SortDirection);
        Assert.NotNull(dto.Filters);
    }

    [Fact]
    public void SearchRequestDto_ShouldSerializeQueryAndPage()
    {
        var dto = new SearchRequestDto { Query = "search term", Page = 2 };

        var json = JsonSerializer.Serialize(dto);

        Assert.Contains("\"query\"", json);
        Assert.Contains("search term", json);
        Assert.Contains("\"page\"", json);
    }

    [Fact]
    public void HealthCheckDto_ShouldSerializeStatus()
    {
        var dto = new HealthCheckDto
        {
            Status = "Healthy",
            Uptime = TimeSpan.FromHours(24)
        };

        var json = JsonSerializer.Serialize(dto);
        Assert.Contains("\"status\"", json);
        Assert.Contains("Healthy", json);
    }

    [Fact]
    public void HealthCheckDto_ShouldHaveEmptyComponentsByDefault()
    {
        var dto = new HealthCheckDto { Status = "Healthy" };

        Assert.NotNull(dto.Components);
        Assert.Empty(dto.Components);
    }

    [Fact]
    public void ComponentHealthDto_ShouldStoreStatus()
    {
        var dto = new ComponentHealthDto
        {
            Status = "Healthy",
            ResponseTime = TimeSpan.FromMilliseconds(50)
        };

        Assert.Equal("Healthy", dto.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(50), dto.ResponseTime);
        Assert.Null(dto.ErrorMessage);
    }

    [Fact]
    public void MetricsSummaryDto_ShouldCalculateCorrectDefaults()
    {
        var dto = new MetricsSummaryDto();

        Assert.Equal(0, dto.TotalCrews);
        Assert.Equal(0, dto.ActiveCrews);
        Assert.Equal(0, dto.TotalTasks);
        Assert.Equal(0, dto.CompletedTasks);
        Assert.Equal(0, dto.FailedTasks);
        Assert.Equal(0.0, dto.SuccessRate);
    }

    [Fact]
    public void ErrorDto_Constructor_ShouldSetField()
    {
        var error = new ErrorDto("CODE", "message", "fieldName");

        Assert.Equal("CODE", error.Code);
        Assert.Equal("message", error.Message);
        Assert.Equal("fieldName", error.Field);
    }

    [Fact]
    public void ApiResponse_Metadata_ShouldDefaultToEmpty()
    {
        var response = ApiResponse<string>.SuccessResponse("data");

        Assert.NotNull(response.Metadata);
    }
}
