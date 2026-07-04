using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Orkeon.Application.Constants.Execution;
using Orkeon.Application.Constants.Pagination;
using Orkeon.Domain.Constants.Platform;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// API-specific DTOs using C# records for request/response patterns.
/// Phase 3.3.3: API DTOs with validation and standardized structure.
/// </summary>

/// <summary>
/// Base API response DTO with common response structure.
/// </summary>
public abstract record ApiResponseDto
{
    /// <summary>
    /// Gets or sets a value indicating whether success.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Gets or sets the timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the request id.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>Gets or sets the version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = PlatformDefaults.Version;
}

/// <summary>
/// Generic API response with typed data.
/// </summary>
public sealed record ApiResponse<T> : ApiResponseDto
{
    /// <summary>Gets or sets the data.</summary>
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    /// <summary>Gets or sets the errors.</summary>
    [JsonPropertyName("errors")]
    public ImmutableList<ErrorDto> Errors { get; init; } = [];

    /// <summary>Gets or sets the metadata.</summary>
    [JsonPropertyName("metadata")]
    public ApiResponseMetadata Metadata { get; init; } = ApiResponseMetadata.Empty;

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    [SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static ApiResponse<T> SuccessResponse(T data, string? requestId = null) => new()
    {
        Success = true,
        Data = data,
        RequestId = requestId
    };

    /// <summary>
    /// Creates an error response.
    /// </summary>
    [SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static ApiResponse<T> ErrorResponse(IEnumerable<ErrorDto> errors, string? requestId = null) => new()
    {
        Success = false,
        Errors = errors.ToImmutableList(),
        RequestId = requestId
    };

    /// <summary>
    /// Creates an error response with a single error.
    /// </summary>
    [SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static ApiResponse<T> ErrorResponse(string errorCode, string errorMessage, string? requestId = null) => new()
    {
        Success = false,
        Errors = ImmutableList.Create(new ErrorDto(errorCode, errorMessage)),
        RequestId = requestId
    };
}

/// <summary>
/// Paginated API response for collection endpoints.
/// </summary>
public sealed record PaginatedResponse<T> : ApiResponseDto
{
    /// <summary>Gets or sets the data.</summary>
    [JsonPropertyName("data")]
    public ImmutableList<T> Data { get; init; } = [];

    /// <summary>Gets or sets the pagination.</summary>
    [JsonPropertyName("pagination")]
    public PaginationDto Pagination { get; init; } = new();

    /// <summary>Gets or sets the total count.</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    /// <summary>Gets or sets the errors.</summary>
    [JsonPropertyName("errors")]
    public ImmutableList<ErrorDto> Errors { get; init; } = [];

    /// <summary>
    /// Creates a successful paginated response.
    /// </summary>
    [SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static PaginatedResponse<T> SuccessResponse(
        IEnumerable<T> data,
        int totalCount,
        int page,
        int pageSize,
        string? requestId = null) => new()
        {
            Success = true,
            Data = data.ToImmutableList(),
            TotalCount = totalCount,
            Pagination = new PaginationDto
            {
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                HasNext = page * pageSize < totalCount,
                HasPrevious = page > 1
            },
            RequestId = requestId
        };
}

/// <summary>
/// Error information DTO for API responses.
/// </summary>
public sealed record ErrorDto
{
    /// <summary>Gets or sets the code.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>Gets or sets the message.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Gets or sets the field.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    /// <summary>Gets or sets the details.</summary>
    [JsonPropertyName("details")]
    public ErrorDetails Details { get; init; } = ErrorDetails.Empty;

    /// <summary>Gets or sets the timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Initializes a new instance of <see cref="ErrorDto"/>.
    /// </summary>
    [SetsRequiredMembers]
    public ErrorDto(string code, string message, string? field = null)
    {
        Code = code;
        Message = message;
        Field = field;
    }

    /// <summary>
    /// Creates a validation error.
    /// </summary>
    public static ErrorDto ValidationError(string field, string message) =>
        new("VALIDATION_ERROR", message, field);

    /// <summary>
    /// Creates a not found error.
    /// </summary>
    public static ErrorDto NotFound(string resource, string identifier) =>
        new("NOT_FOUND", $"{resource} with identifier '{identifier}' was not found");

    /// <summary>
    /// Creates an internal server error.
    /// </summary>
    public static ErrorDto InternalError(string message = "An internal server error occurred") =>
        new("INTERNAL_ERROR", message);

    /// <summary>
    /// Creates an unauthorized error.
    /// </summary>
    public static ErrorDto Unauthorized(string message = "Unauthorized access") =>
        new("UNAUTHORIZED", message);
}

/// <summary>
/// Pagination information DTO.
/// </summary>
public sealed record PaginationDto
{
    /// <summary>Gets or sets the page.</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; } = 1;

    /// <summary>Gets or sets the page size.</summary>
    [JsonPropertyName("page_size")]
    public int PageSize { get; init; } = PaginationDefaults.DefaultPageSize;

    /// <summary>Gets or sets the total pages.</summary>
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether has next.
    /// </summary>
    [JsonPropertyName("has_next")]
    public bool HasNext { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether has previous.
    /// </summary>
    [JsonPropertyName("has_previous")]
    public bool HasPrevious { get; init; }
}

/// <summary>
/// Execution status DTO for tracking execution progress.
/// </summary>
public sealed record ExecutionStatusDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets or sets the crew id.</summary>
    [JsonPropertyName("crew_id")]
    public required string CrewId { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the progress percentage.</summary>
    [JsonPropertyName("progress_percentage")]
    public double ProgressPercentage { get; init; }

    /// <summary>Gets or sets the current task.</summary>
    [JsonPropertyName("current_task")]
    public string? CurrentTask { get; init; }

    /// <summary>Gets or sets the completed tasks.</summary>
    [JsonPropertyName("completed_tasks")]
    public int CompletedTasks { get; init; }

    /// <summary>Gets or sets the total tasks.</summary>
    [JsonPropertyName("total_tasks")]
    public int TotalTasks { get; init; }

    /// <summary>Gets or sets the started at.</summary>
    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; init; }

    /// <summary>Gets or sets the estimated completion.</summary>
    [JsonPropertyName("estimated_completion")]
    public DateTime? EstimatedCompletion { get; init; }

    /// <summary>Gets or sets the errors.</summary>
    [JsonPropertyName("errors")]
    public ImmutableList<ErrorDto> Errors { get; init; } = [];

    /// <summary>Gets or sets the performance metrics.</summary>
    [JsonPropertyName("performance_metrics")]
    public PerformanceMetricsDto? PerformanceMetrics { get; init; }
}

/// <summary>
/// Search/filter request DTO for querying resources.
/// </summary>
public sealed record SearchRequestDto
{
    /// <summary>Gets or sets the query.</summary>
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    /// <summary>Gets or sets the filters.</summary>
    [JsonPropertyName("filters")]
    public SearchFilters Filters { get; init; } = SearchFilters.Empty;

    /// <summary>Gets or sets the sort by.</summary>
    [JsonPropertyName("sort_by")]
    public string? SortBy { get; init; }

    /// <summary>Gets or sets the sort direction.</summary>
    [JsonPropertyName("sort_direction")]
    public string SortDirection { get; init; } = StatusDefaults.DefaultSortDirection;

    /// <summary>Gets or sets the page.</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; } = 1;

    /// <summary>Gets or sets the page size.</summary>
    [JsonPropertyName("page_size")]
    public int PageSize { get; init; } = PaginationDefaults.DefaultPageSize;

    /// <summary>
    /// Gets or sets a value indicating whether include metadata.
    /// </summary>
    [JsonPropertyName("include_metadata")]
    public bool IncludeMetadata { get; init; } = false;
}

/// <summary>
/// Health check response DTO.
/// </summary>
public sealed record HealthCheckDto
{
    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = PlatformDefaults.Version;

    /// <summary>Gets or sets the uptime.</summary>
    [JsonPropertyName("uptime")]
    public TimeSpan Uptime { get; init; }

    /// <summary>Components.</summary>
    [JsonPropertyName("components")]
    public ImmutableDictionary<string, ComponentHealthDto> Components { get; init; } = [];

    /// <summary>Gets or sets the performance metrics.</summary>
    [JsonPropertyName("performance_metrics")]
    public PerformanceMetricsDto? PerformanceMetrics { get; init; }
}

/// <summary>
/// Component health status DTO.
/// </summary>
public sealed record ComponentHealthDto
{
    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the last check.</summary>
    [JsonPropertyName("last_check")]
    public DateTime LastCheck { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the response time.</summary>
    [JsonPropertyName("response_time")]
    public TimeSpan? ResponseTime { get; init; }

    /// <summary>Gets or sets the error message.</summary>
    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; init; }

    /// <summary>Gets or sets the metadata.</summary>
    [JsonPropertyName("metadata")]
    public ComponentHealthMetadata Metadata { get; init; } = ComponentHealthMetadata.Empty;
}

/// <summary>
/// Metrics summary DTO for dashboard and monitoring.
/// </summary>
public sealed record MetricsSummaryDto
{
    /// <summary>Gets or sets the period start.</summary>
    [JsonPropertyName("period_start")]
    public DateTime PeriodStart { get; init; }

    /// <summary>Gets or sets the period end.</summary>
    [JsonPropertyName("period_end")]
    public DateTime PeriodEnd { get; init; }

    /// <summary>Gets or sets the total crews.</summary>
    [JsonPropertyName("total_crews")]
    public int TotalCrews { get; init; }

    /// <summary>Gets or sets the active crews.</summary>
    [JsonPropertyName("active_crews")]
    public int ActiveCrews { get; init; }

    /// <summary>Gets or sets the total tasks.</summary>
    [JsonPropertyName("total_tasks")]
    public int TotalTasks { get; init; }

    /// <summary>Gets or sets the completed tasks.</summary>
    [JsonPropertyName("completed_tasks")]
    public int CompletedTasks { get; init; }

    /// <summary>Gets or sets the failed tasks.</summary>
    [JsonPropertyName("failed_tasks")]
    public int FailedTasks { get; init; }

    /// <summary>Gets or sets the average execution time.</summary>
    [JsonPropertyName("average_execution_time")]
    public TimeSpan AverageExecutionTime { get; init; }

    /// <summary>Gets or sets the success rate.</summary>
    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; init; }

    /// <summary>Gets or sets the resource utilization.</summary>
    [JsonPropertyName("resource_utilization")]
    public ResourceUtilizationDto ResourceUtilization { get; init; } = new();

    /// <summary>Error Distribution.</summary>
    [JsonPropertyName("error_distribution")]
    public ImmutableDictionary<string, int> ErrorDistribution { get; init; } = [];
}

/// <summary>
/// Resource utilization DTO for system monitoring.
/// </summary>
public sealed record ResourceUtilizationSummaryDto
{
    /// <summary>Gets or sets the cpu percentage.</summary>
    [JsonPropertyName("cpu_percentage")]
    public double CpuPercentage { get; init; }

    /// <summary>Gets or sets the memory percentage.</summary>
    [JsonPropertyName("memory_percentage")]
    public double MemoryPercentage { get; init; }

    /// <summary>Gets or sets the memory mb.</summary>
    [JsonPropertyName("memory_mb")]
    public double MemoryMB { get; init; }

    /// <summary>Gets or sets the active threads.</summary>
    [JsonPropertyName("active_threads")]
    public int ActiveThreads { get; init; }

    /// <summary>Gets or sets the database connections.</summary>
    [JsonPropertyName("database_connections")]
    public int DatabaseConnections { get; init; }

    /// <summary>Gets or sets the external api calls.</summary>
    [JsonPropertyName("external_api_calls")]
    public int ExternalApiCalls { get; init; }

    /// <summary>Gets or sets the measured at.</summary>
    [JsonPropertyName("measured_at")]
    public DateTime MeasuredAt { get; init; } = DateTime.UtcNow;
}
