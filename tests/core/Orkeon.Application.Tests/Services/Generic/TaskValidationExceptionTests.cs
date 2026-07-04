using Orkeon.Application.Services.Generic;

namespace Orkeon.Application.Tests.Services.Generic;

/// <summary>
/// Direct coverage for every public constructor of <see cref="TaskValidationException"/>.
/// GenericTaskExecutorTests only reaches the exception indirectly via a validation failure, leaving
/// the parameterless, single-error and inner-exception constructors uncovered.
/// </summary>
public class TaskValidationExceptionTests
{
    [Fact]
    public void Parameterless_HasDefaultMessageAndEmptyErrors()
    {
        var ex = new TaskValidationException();

        Assert.Equal("Task validation failed.", ex.Message);
        Assert.Empty(ex.ValidationErrors);
    }

    [Fact]
    public void ErrorList_FormatsJoinedMessageAndExposesErrors()
    {
        var errors = new[] { "missing description", "missing output" };

        var ex = new TaskValidationException(errors);

        Assert.Equal("Task validation failed: missing description, missing output", ex.Message);
        Assert.Equal(errors, ex.ValidationErrors);
    }

    [Fact]
    public void SingleError_WrapsIntoSingleElementErrorList()
    {
        var ex = new TaskValidationException("bad input");

        Assert.Equal("Task validation failed: bad input", ex.Message);
        Assert.Equal(["bad input"], ex.ValidationErrors);
    }

    [Fact]
    public void MessageWithInnerException_PreservesBothAndEmptyErrors()
    {
        var inner = new InvalidOperationException("root cause");

        var ex = new TaskValidationException("wrapper message", inner);

        Assert.Equal("wrapper message", ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Empty(ex.ValidationErrors);
    }

    [Fact]
    public void IsThrowableAndCatchableAsException()
    {
        static void ThrowIt() => throw new TaskValidationException("fail");

        var caught = Assert.Throws<TaskValidationException>(ThrowIt);

        Assert.IsType<Exception>(caught, exactMatch: false);
    }
}
