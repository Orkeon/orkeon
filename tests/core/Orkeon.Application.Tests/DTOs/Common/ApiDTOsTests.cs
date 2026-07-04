using Orkeon.Application.Common.DTOs;

namespace Orkeon.Application.Tests.DTOs.Common;

public class ApiDTOsTests
{
    private static readonly string[] PaginatedStrings = ["a", "b", "c"];
    private static readonly int[] PaginatedInts12 = [1, 2];
    private static readonly int[] PaginatedInt5 = [5];
    private static readonly string[] PaginatedStringOnly = ["only"];

    // ── ApiResponse<T>.SuccessResponse ──

    [Fact]
    public void ShouldCreateSuccessResponse_WhenCallingSuccessResponseFactory()
    {
        // Act
        var response = ApiResponse<string>.SuccessResponse("payload", "req-42");

        // Assert
        Assert.True(response.Success);
        Assert.Equal("payload", response.Data);
        Assert.Equal("req-42", response.RequestId);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void ShouldSetDefaultTimestamp_WhenCreatingResponse()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var response = ApiResponse<int>.SuccessResponse(42);

        // Assert
        Assert.InRange(response.Timestamp, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal("1.0.0", response.Version);
    }

    [Fact]
    public void ShouldSetNullRequestId_WhenNotProvidedInSuccessResponse()
    {
        // Act
        var response = ApiResponse<string>.SuccessResponse("data");

        // Assert
        Assert.Null(response.RequestId);
    }

    // ── ApiResponse<T>.ErrorResponse (single) ──

    [Fact]
    public void ShouldCreateSingleErrorResponse_WhenCallingErrorResponseWithCodeAndMessage()
    {
        // Act
        var response = ApiResponse<string>.ErrorResponse("ERR_01", "Something failed", "req-99");

        // Assert
        Assert.False(response.Success);
        Assert.Single(response.Errors);
        Assert.Equal("ERR_01", response.Errors[0].Code);
        Assert.Equal("Something failed", response.Errors[0].Message);
        Assert.Equal("req-99", response.RequestId);
        Assert.Null(response.Data);
    }

    // ── ApiResponse<T>.ErrorResponse (multiple) ──

    [Fact]
    public void ShouldCreateMultipleErrorResponse_WhenCallingErrorResponseWithCollection()
    {
        // Arrange
        var errors = new[]
        {
            new ErrorDto("E1", "First"),
            new ErrorDto("E2", "Second")
        };

        // Act
        var response = ApiResponse<int>.ErrorResponse(errors);

        // Assert
        Assert.False(response.Success);
        Assert.Equal(2, response.Errors.Count);
        Assert.Equal("E1", response.Errors[0].Code);
        Assert.Equal("E2", response.Errors[1].Code);
    }

    // ── PaginatedResponse<T> ──

    [Fact]
    public void ShouldCalculateTotalPages_WhenCreatingPaginatedResponse()
    {
        // Act
        var response = PaginatedResponse<string>.SuccessResponse(
            PaginatedStrings, totalCount: 10, page: 1, pageSize: 3);

        // Assert
        Assert.True(response.Success);
        Assert.Equal(3, response.Data.Count);
        Assert.Equal(10, response.TotalCount);
        Assert.Equal(4, response.Pagination.TotalPages); // ceil(10/3) = 4
    }

    [Fact]
    public void ShouldSetHasNext_WhenMorePagesExist()
    {
        // Act
        var response = PaginatedResponse<int>.SuccessResponse(
            PaginatedInts12, totalCount: 5, page: 1, pageSize: 2);

        // Assert
        Assert.True(response.Pagination.HasNext);   // 1*2 < 5
        Assert.False(response.Pagination.HasPrevious); // page == 1
    }

    [Fact]
    public void ShouldSetHasPrevious_WhenOnLaterPage()
    {
        // Act
        var response = PaginatedResponse<int>.SuccessResponse(
            PaginatedInt5, totalCount: 5, page: 3, pageSize: 2);

        // Assert
        Assert.False(response.Pagination.HasNext);    // 3*2 >= 5
        Assert.True(response.Pagination.HasPrevious);  // page > 1
    }

    [Fact]
    public void ShouldHandleSinglePage_WhenTotalCountFitsInOnePage()
    {
        // Act
        var response = PaginatedResponse<string>.SuccessResponse(
            PaginatedStringOnly, totalCount: 1, page: 1, pageSize: 10);

        // Assert
        Assert.Equal(1, response.Pagination.TotalPages);
        Assert.False(response.Pagination.HasNext);
        Assert.False(response.Pagination.HasPrevious);
    }

    [Fact]
    public void ShouldSetRequestId_WhenProvidedInPaginatedResponse()
    {
        // Act
        var response = PaginatedResponse<int>.SuccessResponse(
            Array.Empty<int>(), totalCount: 0, page: 1, pageSize: 10, requestId: "paged-1");

        // Assert
        Assert.Equal("paged-1", response.RequestId);
    }

    // ── ErrorDto factories ──

    [Fact]
    public void ShouldCreateValidationError_WhenCallingFactory()
    {
        // Act
        var error = ErrorDto.ValidationError("email", "Invalid format");

        // Assert
        Assert.Equal("VALIDATION_ERROR", error.Code);
        Assert.Equal("Invalid format", error.Message);
        Assert.Equal("email", error.Field);
    }

    [Fact]
    public void ShouldCreateNotFoundError_WhenCallingFactory()
    {
        // Act
        var error = ErrorDto.NotFound("Agent", "abc-123");

        // Assert
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Contains("Agent", error.Message);
        Assert.Contains("abc-123", error.Message);
    }

    [Fact]
    public void ShouldCreateInternalError_WhenCallingFactoryWithDefaultMessage()
    {
        // Act
        var error = ErrorDto.InternalError();

        // Assert
        Assert.Equal("INTERNAL_ERROR", error.Code);
        Assert.Equal("An internal server error occurred", error.Message);
    }

    [Fact]
    public void ShouldCreateInternalError_WhenCallingFactoryWithCustomMessage()
    {
        // Act
        var error = ErrorDto.InternalError("Disk full");

        // Assert
        Assert.Equal("INTERNAL_ERROR", error.Code);
        Assert.Equal("Disk full", error.Message);
    }

    [Fact]
    public void ShouldCreateUnauthorizedError_WhenCallingFactoryWithDefaultMessage()
    {
        // Act
        var error = ErrorDto.Unauthorized();

        // Assert
        Assert.Equal("UNAUTHORIZED", error.Code);
        Assert.Equal("Unauthorized access", error.Message);
    }

    [Fact]
    public void ShouldCreateUnauthorizedError_WhenCallingFactoryWithCustomMessage()
    {
        // Act
        var error = ErrorDto.Unauthorized("Token expired");

        // Assert
        Assert.Equal("UNAUTHORIZED", error.Code);
        Assert.Equal("Token expired", error.Message);
    }

    [Fact]
    public void ShouldSetTimestamp_WhenCreatingErrorDto()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var error = new ErrorDto("X", "msg");

        // Assert
        Assert.InRange(error.Timestamp, before, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void ShouldDefaultToEmptyDetails_WhenCreatingErrorDto()
    {
        // Act
        var error = new ErrorDto("X", "msg");

        // Assert
        Assert.NotNull(error.Details);
        Assert.Equal(0, error.Details.Count);
    }

    // ── PaginationDto defaults ──

    [Fact]
    public void ShouldSetPaginationDefaults_WhenCreatingNewPaginationDto()
    {
        // Act
        var dto = new PaginationDto();

        // Assert
        Assert.Equal(1, dto.Page);
        Assert.Equal(20, dto.PageSize);
        Assert.Equal(0, dto.TotalPages);
        Assert.False(dto.HasNext);
        Assert.False(dto.HasPrevious);
    }
}
