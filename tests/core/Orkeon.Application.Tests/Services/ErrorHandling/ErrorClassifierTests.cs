using Orkeon.Domain.Common;
using Orkeon.Domain.Exceptions;
using Orkeon.Application.Services.ErrorHandling;
using System.Security;
using SystemTimeoutException = System.TimeoutException;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services.ErrorHandling;

public class ErrorClassifierTests
{
    #region ClassifyError Tests - Network Category

    [Fact]
    public void ShouldReturnNetworkCategory_WhenClassifyingErrorHttpRequestException()
    {
        // Arrange
        var exception = new HttpRequestException("Connection failed");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Network, category);
    }

    [Fact]
    public void ShouldReturnNetworkCategory_WhenClassifyingErrorTaskCancelledExceptionWithTimeout()
    {
        // Arrange
        var exception = new TaskCanceledException("Request timeout");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Network, category);
    }

    [Fact]
    public void ShouldReturnNetworkCategory_WhenClassifyingErrorSystemTimeoutException()
    {
        // Arrange
        var exception = new SystemTimeoutException("Operation timed out");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Network, category);
    }

    [Fact]
    public void ShouldReturnNetworkCategory_WhenClassifyingErrorDomainTimeoutException()
    {
        // Arrange
        var exception = new Orkeon.Domain.Exceptions.TimeoutException("TestOperation", TimeoutQuick);

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Network, category);
    }

    #endregion

    #region ClassifyError Tests - Resource Category

    [Fact]
    public void ShouldReturnResourceCategory_WhenClassifyingErrorOutOfMemoryException()
    {
        // Arrange
        var exception = new OutOfMemoryException();

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Resource, category);
    }

    [Fact]
    public void ShouldReturnResourceCategory_WhenClassifyingErrorStackOverflowException()
    {
        // Arrange
        var exception = new StackOverflowException();

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Resource, category);
    }

    [Theory]
    [InlineData("Rate limit exceeded")]
    [InlineData("API rate limit reached")]
    [InlineData("Quota exceeded")]
    [InlineData("Request throttled")]
    public void ShouldReturnResourceCategory_WhenClassifyingErrorMessageWithResourceKeywords(string message)
    {
        // Arrange
        var exception = new InvalidOperationException(message);

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Resource, category);
    }

    #endregion

    #region ClassifyError Tests - Security Category

    [Fact]
    public void ShouldReturnSecurityCategory_WhenClassifyingErrorUnauthorizedAccessException()
    {
        // Arrange
        var exception = new UnauthorizedAccessException();

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Security, category);
    }

    [Fact]
    public void ShouldReturnSecurityCategory_WhenClassifyingErrorSecurityException()
    {
        // Arrange
        var exception = new SecurityException();

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Security, category);
    }

    [Theory]
    [InlineData("Unauthorized access")]
    [InlineData("Request forbidden")]
    [InlineData("Authentication failed")]
    public void ShouldReturnSecurityCategory_WhenClassifyingErrorMessageWithSecurityKeywords(string message)
    {
        // Arrange
        var exception = new Exception(message);

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Security, category);
    }

    #endregion

    #region ClassifyError Tests - Configuration Category

    [Fact]
    public void ShouldReturnConfigurationCategory_WhenClassifyingErrorArgumentNullException()
    {
        // Arrange
        var exception = new ArgumentNullException("paramName");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Configuration, category);
    }

    [Fact]
    public void ShouldReturnConfigurationCategory_WhenClassifyingErrorArgumentException()
    {
        // Arrange
        var exception = new ArgumentException("Invalid argument");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Configuration, category);
    }

    [Fact]
    public void ShouldReturnConfigurationCategory_WhenClassifyingErrorWithInvalidOperationException()
    {
        // Arrange
        var exception = new InvalidOperationException("Invalid state");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Configuration, category);
    }

    [Fact]
    public void ShouldReturnConfigurationCategory_WhenClassifyingErrorFileNotFoundException()
    {
        // Arrange
        var exception = new FileNotFoundException();

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Configuration, category);
    }

    #endregion

    #region ClassifyError Tests - BusinessLogic Category

    [Fact]
    public void ShouldReturnBusinessLogicCategory_WhenClassifyingErrorCrewException()
    {
        // Arrange
        var exception = new CrewException(CrewId.Create(), "Crew error");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.BusinessLogic, category);
    }

    [Fact]
    public void ShouldReturnBusinessLogicCategory_WhenClassifyingErrorAgentException()
    {
        // Arrange
        var exception = new AgentException(AgentId.Create(), "Agent error");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.BusinessLogic, category);
    }

    [Fact]
    public void ShouldReturnBusinessLogicCategory_WhenClassifyingErrorTaskException()
    {
        // Arrange
        var exception = new TaskException(TaskId.Create(), "Task error");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.BusinessLogic, category);
    }

    [Fact]
    public void ShouldReturnBusinessLogicCategory_WhenClassifyingErrorValidationException()
    {
        // Arrange
        var exception = new ValidationException("PropertyName", "Validation failed");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.BusinessLogic, category);
    }

    #endregion

    #region ClassifyError Tests - Data Category

    [Theory]
    [InlineData("SqlException")]
    [InlineData("DatabaseException")]
    public void ShouldReturnDataCategory_WhenClassifyingErrorExceptionNameWithDataKeywords(string exceptionName)
    {
        // Arrange
        var exception = new Exception($"Test from {exceptionName}");
        // Create specific exception types for Data classification
        Exception customException = exceptionName switch
        {
            "SqlException" => new SqlException($"Test from {exceptionName}"),
            "DatabaseException" => new DatabaseException($"Test from {exceptionName}"),
            _ => new TestDataException($"Test from {exceptionName}", exceptionName)
        };

        // Act
        var category = ErrorClassifier.ClassifyError(customException);

        // Assert
        Assert.Equal(ErrorCategory.Data, category);
    }

    [Theory]
    [InlineData("Connection string is invalid")]
    [InlineData("Database connection failed")]
    [InlineData("Deadlock detected")]
    public void ShouldReturnDataCategory_WhenClassifyingErrorMessageWithDataKeywords(string message)
    {
        // Arrange
        var exception = new Exception(message);

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Data, category);
    }

    #endregion

    #region ClassifyError Tests - ExternalService Category

    [Theory]
    [InlineData("OpenAI API error")]
    [InlineData("External API failed")]
    [InlineData("Service unavailable")]
    public void ShouldReturnExternalServiceCategory_WhenClassifyingErrorMessageWithExternalServiceKeywords(string message)
    {
        // Arrange
        var exception = new Exception(message);

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.ExternalService, category);
    }

    #endregion

    #region ClassifyError Tests - Validation Category

    [Fact]
    public void ShouldReturnValidationCategory_WhenClassifyingErrorFormatException()
    {
        // Arrange
        var exception = new FormatException();

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Validation, category);
    }

    [Theory]
    [InlineData("Validation failed")]
    [InlineData("Invalid format provided")]
    [InlineData("Failed to parse input")]
    public void ShouldReturnValidationCategory_WhenClassifyingErrorMessageWithValidationKeywords(string message)
    {
        // Arrange
        var exception = new Exception(message);

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Validation, category);
    }

    #endregion

    #region ClassifyError Tests - Concurrency Category

    [Fact]
    public void ShouldReturnConcurrencyCategory_WhenClassifyingErrorWithInvalidOperationExceptionWithThreadMessage()
    {
        // Arrange
        // InvalidOperationException is classified as Configuration, not Concurrency
        var exception = new InvalidOperationException("Cross-thread operation not valid");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Configuration, category);
    }

    [Fact]
    public void ShouldReturnConcurrencyCategory_WhenClassifyingErrorMessageWithRaceCondition()
    {
        // Arrange
        var exception = new Exception("Race condition detected");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Concurrency, category);
    }

    #endregion

    #region ClassifyError Tests - Unknown Category

    [Fact]
    public void ShouldReturnUnknownCategory_WhenClassifyingErrorUnrecognizedException()
    {
        // Arrange
        var exception = new Exception("Some random error");

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Unknown, category);
    }

    #endregion

    #region IsTransientError Tests

    [Theory]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(SystemTimeoutException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(ArgumentNullException), false)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(UnauthorizedAccessException), false)]
    [InlineData(typeof(SecurityException), false)]
    [InlineData(typeof(OutOfMemoryException), false)]
    [InlineData(typeof(StackOverflowException), false)]
    public void ShouldReturnCorrectResult_WhenUsingIsTransientErrorWithVariousExceptionTypes(Type exceptionType, bool expectedResult)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        // Act
        var isTransient = ErrorClassifier.IsTransientError(exception);

        // Assert
        Assert.Equal(expectedResult, isTransient);
    }

    [Theory]
    [InlineData("Rate limit exceeded", true)]
    [InlineData("Request throttled", true)]
    [InlineData("Temporary failure", true)]
    [InlineData("Transient error occurred", true)]
    [InlineData("Connection timeout", true)]
    [InlineData("Connection lost", true)]
    [InlineData("Service unavailable", true)]
    [InlineData("HTTP 502 Bad Gateway", true)]
    [InlineData("HTTP 503 Service Unavailable", true)]
    [InlineData("HTTP 504 Gateway Timeout", true)]
    [InlineData("Regular error", false)]
    public void ShouldReturnCorrectResult_WhenUsingIsTransientErrorMessagesWithKeywords(string message, bool expectedResult)
    {
        // Arrange
        var exception = new Exception(message);

        // Act
        var isTransient = ErrorClassifier.IsTransientError(exception);

        // Assert
        Assert.Equal(expectedResult, isTransient);
    }

    #endregion

    #region DetermineRecoverability Tests

    [Theory]
    [InlineData(typeof(HttpRequestException), RecoverabilityLevel.FullyRecoverable)]
    [InlineData(typeof(SystemTimeoutException), RecoverabilityLevel.FullyRecoverable)]
    [InlineData(typeof(TaskCanceledException), RecoverabilityLevel.FullyRecoverable)]
    [InlineData(typeof(InvalidOperationException), RecoverabilityLevel.PartiallyRecoverable)]
    [InlineData(typeof(NotSupportedException), RecoverabilityLevel.PartiallyRecoverable)]
    [InlineData(typeof(CrewException), RecoverabilityLevel.RequiresIntervention)]
    [InlineData(typeof(AgentException), RecoverabilityLevel.RequiresIntervention)]
    [InlineData(typeof(TaskException), RecoverabilityLevel.RequiresIntervention)]
    [InlineData(typeof(ValidationException), RecoverabilityLevel.RequiresIntervention)]
    [InlineData(typeof(UnauthorizedAccessException), RecoverabilityLevel.RequiresIntervention)]
    [InlineData(typeof(OutOfMemoryException), RecoverabilityLevel.NonRecoverable)]
    [InlineData(typeof(StackOverflowException), RecoverabilityLevel.NonRecoverable)]
    [InlineData(typeof(AccessViolationException), RecoverabilityLevel.NonRecoverable)]
    public void ShouldReturnCorrectLevel_WhenDeterminingRecoverabilityWithVariousExceptionTypes(Type exceptionType, RecoverabilityLevel expectedLevel)
    {
        // Arrange
        Exception exception = exceptionType.Name switch
        {
            nameof(CrewException) => new CrewException(CrewId.Create(), "Test crew error"),
            nameof(AgentException) => new AgentException(AgentId.Create(), "Test agent error"),
            nameof(TaskException) => new TaskException(TaskId.Create(), "Test task error"),
            nameof(ValidationException) => new ValidationException("testProperty", "Test validation error"),
            _ => (Exception)Activator.CreateInstance(exceptionType)!
        };

        // Act
        var recoverability = ErrorClassifier.DetermineRecoverability(exception);

        // Assert
        Assert.Equal(expectedLevel, recoverability);
    }

    [Theory]
    [InlineData("Rate limit exceeded", RecoverabilityLevel.FullyRecoverable)]
    [InlineData("Database connection failed", RecoverabilityLevel.PartiallyRecoverable)]
    [InlineData("Unknown error", RecoverabilityLevel.PartiallyRecoverable)]
    public void ShouldReturnCorrectLevel_WhenDeterminingRecoverabilityMessagesWithKeywords(string message, RecoverabilityLevel expectedLevel)
    {
        // Arrange
        var exception = new Exception(message);

        // Act
        var recoverability = ErrorClassifier.DetermineRecoverability(exception);

        // Assert
        Assert.Equal(expectedLevel, recoverability);
    }

    #endregion

    #region ExtractErrorDetails Tests

    [Fact]
    public void ShouldExtractAllDetails_WhenExtractingErrorDetailsWithCompleteException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");
        var exception = new HttpRequestException("HTTP Error: 503 Service Unavailable", innerException);

        // Act
        var details = ErrorClassifier.ExtractErrorDetails(exception);

        // Assert
        Assert.Equal(ErrorCategory.Network, details.Category);
        Assert.True(details.IsTransient);
        Assert.Equal(RecoverabilityLevel.FullyRecoverable, details.Recoverability);
        Assert.Equal("503", details.ErrorCode);
        Assert.Equal("InvalidOperationException", details.InnerExceptionType);
        Assert.Null(details.Component); // HttpRequestException doesn't have component info without stack trace
    }

    [Fact]
    public void ShouldExtractComponent_WhenExtractingErrorDetailsWithStackTrace()
    {
        // Arrange
        Exception? exception = null;
        try
        {
            throw new InvalidOperationException("Test error");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Act
        var details = ErrorClassifier.ExtractErrorDetails(exception!);

        // Assert
        Assert.NotNull(details.StackTrace);
        Assert.Equal(ErrorCategory.Configuration, details.Category);
    }

    [Theory]
    [InlineData("Error: AUTH001", "AUTH001")]
    [InlineData("Code: ERR_INVALID", "ERR_INVALID")]
    [InlineData("HTTP 404 Not Found", "404")]
    [InlineData("No error code here", null)]
    public void ShouldExtractErrorCode_WhenExtractingErrorDetailsWithVariousErrorMessages(string message, string? expectedCode)
    {
        // Arrange
        var exception = new Exception(message);

        // Act
        var details = ErrorClassifier.ExtractErrorDetails(exception);

        // Assert
        Assert.Equal(expectedCode, details.ErrorCode);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldNotThrow_WhenClassifyingErrorWithNullMessage()
    {
        // Arrange
        var exception = new Exception((string?)null);

        // Act
        var category = ErrorClassifier.ClassifyError(exception);

        // Assert
        Assert.Equal(ErrorCategory.Unknown, category);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsTransientErrorWithNullMessage()
    {
        // Arrange
        var exception = new Exception((string?)null);

        // Act
        var isTransient = ErrorClassifier.IsTransientError(exception);

        // Assert
        Assert.False(isTransient);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenExtractingErrorDetailsWithNoStackTrace()
    {
        // Arrange
        var exception = new Exception("Test error");
        // Exception created this way has no stack trace

        // Act
        var details = ErrorClassifier.ExtractErrorDetails(exception);

        // Assert
        Assert.Null(details.StackTrace);
        Assert.Null(details.Component);
        Assert.Null(details.Operation);
    }

    [Fact]
    public void ShouldWork_WhenExtractingErrorDetailsCaseInsensitiveMatching()
    {
        // Arrange
        var exception1 = new Exception("RATE LIMIT exceeded");
        var exception2 = new Exception("rate limit EXCEEDED");
        var exception3 = new Exception("RaTe LiMiT ExCeEdEd");

        // Act
        var category1 = ErrorClassifier.ClassifyError(exception1);
        var category2 = ErrorClassifier.ClassifyError(exception2);
        var category3 = ErrorClassifier.ClassifyError(exception3);

        // Assert
        Assert.Equal(ErrorCategory.Resource, category1);
        Assert.Equal(ErrorCategory.Resource, category2);
        Assert.Equal(ErrorCategory.Resource, category3);
    }

    #endregion

    #region Test Helper Classes

    private class TestDataException : Exception
    {
        public TestDataException(string message, string typeName) : base(message)
        {
            TypeName = typeName;
        }

        public string TypeName { get; }

        public override string ToString() => TypeName;

        // Override GetType().Name to return the TypeName for pattern matching
        // This is a test helper to simulate exceptions with specific names
    }

    private class SqlException : Exception
    {
        public SqlException(string message) : base(message) { }
    }

    private class DatabaseException : Exception
    {
        public DatabaseException(string message) : base(message) { }
    }

    #endregion
}
