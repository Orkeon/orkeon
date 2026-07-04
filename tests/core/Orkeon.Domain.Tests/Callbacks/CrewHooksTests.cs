using Orkeon.Application.Callback;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Callbacks;

/// <summary>
/// Tests for CrewHooks following Clean Architecture principles.
/// Tests the crew hook implementations for before and after kickoff events.
/// </summary>
public class CrewHooksTests
{
    #region Test Helpers

    private static DomainCrew CreateTestCrew()
    {
        // Create a minimal crew for testing purposes
        return DomainCrew.Create(
            goal: "Test Crew Goal",
            processType: ProcessType.Sequential);
    }

    private static async System.Threading.Tasks.Task<bool> TestExecuteFunction(DomainCrew crew)
    {
        await System.Threading.Tasks.Task.Delay(1); // Simulate async work
        return true;
    }

    private static async System.Threading.Tasks.Task<string> TestExecuteFunctionWithResult(DomainCrew crew, object? result)
    {
        await System.Threading.Tasks.Task.Delay(1); // Simulate async work
        return $"Processed crew with result: {result}";
    }

    #endregion

    #region BeforeKickoffHook Tests

    [Fact]
    public void ShouldHaveDefaultValues_WhenUsingBeforeKickoffHookWithDefaultConstructor()
    {
        // Act
        var hook = new BeforeKickoffHook();

        // Assert
        Assert.Equal(string.Empty, hook.Name);
        Assert.NotNull(hook.Execute);
        Assert.Equal(0, hook.Priority);
        Assert.True(hook.ContinueOnError);
    }

    [Fact]
    public void ShouldReturnCompletedTask_WhenUsingBeforeKickoffHookWithDefaultExecute()
    {
        // Arrange
        var hook = new BeforeKickoffHook();
        var crew = CreateTestCrew();

        // Act
        var task = hook.Execute(crew);

        // Assert
        Assert.NotNull(task);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(System.Threading.Tasks.TaskStatus.RanToCompletion, task.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeSettable_WhenUsingBeforeKickoffHookUsingProperties()
    {
        // Arrange
        var crew = CreateTestCrew();
        var executed = false;

        // Act
        var hook = new BeforeKickoffHook
        {
            Name = "Test Hook",
            Priority = 10,
            ContinueOnError = false,
            Execute = async _ =>
            {
                executed = true;
                await System.Threading.Tasks.Task.CompletedTask;
            }
        };

        // Assert
        Assert.Equal("Test Hook", hook.Name);
        Assert.Equal(10, hook.Priority);
        Assert.False(hook.ContinueOnError);

        // Test execute function
        await hook.Execute(crew);
        Assert.True(executed);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteCorrectly_WhenUsingBeforeKickoffHookWithCustomExecuteFunction()
    {
        // Arrange
        var crew = CreateTestCrew();
        var executionLog = new List<string>();

        var hook = new BeforeKickoffHook
        {
            Execute = async c =>
            {
                executionLog.Add($"Executing before kickoff for crew: {c.GetType().Name}");
                await System.Threading.Tasks.Task.Delay(10);
                executionLog.Add("Before kickoff completed");
            }
        };

        // Act
        await hook.Execute(crew);

        // Assert
        Assert.Equal(2, executionLog.Count);
        Assert.Contains("Executing before kickoff for crew: Crew", executionLog);
        Assert.Contains("Before kickoff completed", executionLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAwaitCorrectly_WhenUsingBeforeKickoffHookWithAsyncExecuteFunction()
    {
        // Arrange
        var crew = CreateTestCrew();
        var startTime = DateTime.UtcNow;

        var hook = new BeforeKickoffHook
        {
            Execute = async _ =>
            {
                await System.Threading.Tasks.Task.Delay(100); // Simulate async work
            }
        };

        // Act
        await hook.Execute(crew);

        // Assert
        var duration = DateTime.UtcNow - startTime;
        Assert.True(duration.TotalMilliseconds >= 90); // Allow some variance
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenUsingBeforeKickoffHookWithExceptionInExecute()
    {
        // Arrange
        var crew = CreateTestCrew();

        var hook = new BeforeKickoffHook
        {
            Execute = _ => throw new InvalidOperationException("Test exception in before kickoff")
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => hook.Execute(crew));
        Assert.Equal("Test exception in before kickoff", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple Hook")]
    [InlineData("Complex Hook Name with Spaces")]
    [InlineData("Hook_With_Underscores")]
    [InlineData("Hook-With-Dashes")]
    [InlineData("Hook123WithNumbers")]
    public void ShouldAcceptAll_WhenUsingBeforeKickoffHookWithVariousNames(string hookName)
    {
        // Act
        var hook = new BeforeKickoffHook { Name = hookName };

        // Assert
        Assert.Equal(hookName, hook.Name);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-100)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void ShouldAcceptAll_WhenUsingBeforeKickoffHookWithVariousPriorities(int priority)
    {
        // Act
        var hook = new BeforeKickoffHook { Priority = priority };

        // Assert
        Assert.Equal(priority, hook.Priority);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldAcceptAll_WhenUsingBeforeKickoffHookWithContinueOnErrorValues(bool continueOnError)
    {
        // Act
        var hook = new BeforeKickoffHook { ContinueOnError = continueOnError };

        // Assert
        Assert.Equal(continueOnError, hook.ContinueOnError);
    }

    #endregion

    #region AfterKickoffHook Tests

    [Fact]
    public void ShouldHaveDefaultValues_WhenUsingAfterKickoffHookWithDefaultConstructor()
    {
        // Act
        var hook = new AfterKickoffHook();

        // Assert
        Assert.Equal(string.Empty, hook.Name);
        Assert.NotNull(hook.Execute);
        Assert.Equal(0, hook.Priority);
        Assert.True(hook.ContinueOnError);
    }

    [Fact]
    public void ShouldReturnCompletedTask_WhenUsingAfterKickoffHookWithDefaultExecute()
    {
        // Arrange
        var hook = new AfterKickoffHook();
        var crew = CreateTestCrew();
        var result = "test result";

        // Act
        var task = hook.Execute(crew, result);

        // Assert
        Assert.NotNull(task);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(System.Threading.Tasks.TaskStatus.RanToCompletion, task.Status);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeSettable_WhenUsingAfterKickoffHookUsingProperties()
    {
        // Arrange
        var crew = CreateTestCrew();
        var result = "test result";
        var executed = false;

        // Act
        var hook = new AfterKickoffHook
        {
            Name = "After Test Hook",
            Priority = 20,
            ContinueOnError = false,
            Execute = async (_, __) =>
            {
                executed = true;
                await System.Threading.Tasks.Task.CompletedTask;
            }
        };

        // Assert
        Assert.Equal("After Test Hook", hook.Name);
        Assert.Equal(20, hook.Priority);
        Assert.False(hook.ContinueOnError);

        // Test execute function
        await hook.Execute(crew, result);
        Assert.True(executed);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteCorrectly_WhenUsingAfterKickoffHookWithCustomExecuteFunction()
    {
        // Arrange
        var crew = CreateTestCrew();
        var result = new { Status = Success, Data = "Test Data" };
        var executionLog = new List<string>();

        var hook = new AfterKickoffHook
        {
            Execute = async (c, r) =>
            {
                executionLog.Add($"Executing after kickoff for crew: {c.GetType().Name}");
                executionLog.Add($"Result type: {r?.GetType().Name ?? "null"}");
                await System.Threading.Tasks.Task.Delay(10);
                executionLog.Add("After kickoff completed");
            }
        };

        // Act
        await hook.Execute(crew, result);

        // Assert
        Assert.Equal(3, executionLog.Count);
        Assert.Contains("Executing after kickoff for crew: Crew", executionLog);
        Assert.Contains("Result type: <>f__AnonymousType", executionLog[1]); // Anonymous type
        Assert.Contains("After kickoff completed", executionLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingAfterKickoffHookWithNullResult()
    {
        // Arrange
        var crew = CreateTestCrew();
        var executionCount = 0;

        var hook = new AfterKickoffHook
        {
            Execute = async (c, r) =>
            {
                Assert.NotNull(c);
                Assert.Null(r);
                executionCount++;
                await System.Threading.Tasks.Task.CompletedTask;
            }
        };

        // Act
        await hook.Execute(crew, null);

        // Assert
        Assert.Equal(1, executionCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("string result")]
    [InlineData(42)]
    [InlineData(true)]
    [InlineData(3.14)]
    public async System.Threading.Tasks.Task ShouldAcceptAll_WhenUsingAfterKickoffHookWithVariousResultTypes(object? result)
    {
        // Arrange
        var crew = CreateTestCrew();
        object? capturedResult = "not set";

        var hook = new AfterKickoffHook
        {
            Execute = async (c, r) =>
            {
                capturedResult = r;
                await System.Threading.Tasks.Task.CompletedTask;
            }
        };

        // Act
        await hook.Execute(crew, result);

        // Assert
        Assert.Equal(result, capturedResult);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingAfterKickoffHookWithComplexResult()
    {
        // Arrange
        var crew = CreateTestCrew();
        var complexResult = new
        {
            TaskResults = new[] { "result1", "result2", "result3" },
            Metadata = new Dictionary<string, object>
            {
                { "duration", TimeoutStandard },
                { "success", true },
                { "agentCount", 3 }
            },
            CreatedAt = DateTime.UtcNow
        };

        var processedResult = "";

        var hook = new AfterKickoffHook
        {
            Execute = async (c, r) =>
            {
                if (r != null)
                {
                    processedResult = $"Processed complex result with {r.GetType().GetProperties().Length} properties";
                }
                await System.Threading.Tasks.Task.CompletedTask;
            }
        };

        // Act
        await hook.Execute(crew, complexResult);

        // Assert
        Assert.Contains("Processed complex result", processedResult);
        Assert.Contains("properties", processedResult);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAwaitCorrectly_WhenUsingAfterKickoffHookWithAsyncExecuteFunction()
    {
        // Arrange
        var crew = CreateTestCrew();
        var result = "test result";
        var startTime = DateTime.UtcNow;

        var hook = new AfterKickoffHook
        {
            Execute = async (_, __) =>
            {
                await System.Threading.Tasks.Task.Delay(100); // Simulate async work
            }
        };

        // Act
        await hook.Execute(crew, result);

        // Assert
        var duration = DateTime.UtcNow - startTime;
        Assert.True(duration.TotalMilliseconds >= 90); // Allow some variance
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPropagateException_WhenUsingAfterKickoffHookWithExceptionInExecute()
    {
        // Arrange
        var crew = CreateTestCrew();
        var result = "test result";

        var hook = new AfterKickoffHook
        {
            Execute = (_, __) => throw new InvalidOperationException("Test exception in after kickoff")
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => hook.Execute(crew, result));
        Assert.Equal("Test exception in after kickoff", exception.Message);
    }

    #endregion

    #region Integration and Comparison Tests

    [Fact]
    public void ShouldHaveSimilarStructure_WhenUsingBeforeAndAfterKickoffHooks()
    {
        // Arrange
        var beforeHook = new BeforeKickoffHook();
        var afterHook = new AfterKickoffHook();

        // Act & Assert
        Assert.Equal(beforeHook.Name, afterHook.Name);
        Assert.Equal(beforeHook.Priority, afterHook.Priority);
        Assert.Equal(beforeHook.ContinueOnError, afterHook.ContinueOnError);

        // Both should have non-null Execute functions
        Assert.NotNull(beforeHook.Execute);
        Assert.NotNull(afterHook.Execute);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingCrewHooksInCollection()
    {
        // Arrange
        var beforeHooks = new List<BeforeKickoffHook>
        {
            new() { Name = "Hook1", Priority = 1 },
            new() { Name = "Hook2", Priority = 10 },
            new() { Name = "Hook3", Priority = 5 }
        };

        var afterHooks = new List<AfterKickoffHook>
        {
            new() { Name = "AfterHook1", Priority = 2 },
            new() { Name = "AfterHook2", Priority = 8 }
        };

        // Act
        var sortedBeforeHooks = beforeHooks.OrderBy(h => h.Priority).ToList();
        var sortedAfterHooks = afterHooks.OrderByDescending(h => h.Priority).ToList();

        // Assert
        Assert.Equal(3, beforeHooks.Count);
        Assert.Equal(2, afterHooks.Count);

        Assert.Equal("Hook1", sortedBeforeHooks[0].Name);
        Assert.Equal("Hook3", sortedBeforeHooks[1].Name);
        Assert.Equal("Hook2", sortedBeforeHooks[2].Name);

        Assert.Equal("AfterHook2", sortedAfterHooks[0].Name);
        Assert.Equal("AfterHook1", sortedAfterHooks[1].Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectPriority_WhenUsingCrewHooksUsingExecutionSequence()
    {
        // Arrange
        var executionOrder = new List<string>();
        var crew = CreateTestCrew();

        var hooks = new List<BeforeKickoffHook>
        {
            new()
            {
                Name = "High Priority",
                Priority = 100,
                Execute = async _ =>
                {
                    executionOrder.Add("High Priority");
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            },
            new()
            {
                Name = "Low Priority",
                Priority = 1,
                Execute = async _ =>
                {
                    executionOrder.Add("Low Priority");
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            },
            new()
            {
                Name = "Medium Priority",
                Priority = 50,
                Execute = async _ =>
                {
                    executionOrder.Add("Medium Priority");
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
        };

        // Act - Execute in priority order (ascending)
        var sortedHooks = hooks.OrderBy(h => h.Priority);
        foreach (var hook in sortedHooks)
        {
            await hook.Execute(crew);
        }

        // Assert
        Assert.Equal(3, executionOrder.Count);
        Assert.Equal("Low Priority", executionOrder[0]);
        Assert.Equal("Medium Priority", executionOrder[1]);
        Assert.Equal("High Priority", executionOrder[2]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueAfterException_WhenUsingCrewHooksWithContinueOnErrorTrue()
    {
        // Arrange
        var executionLog = new List<string>();
        var crew = CreateTestCrew();

        var hooks = new List<BeforeKickoffHook>
        {
            new()
            {
                Name = "Working Hook 1",
                ContinueOnError = true,
                Execute = async _ =>
                {
                    executionLog.Add("Working Hook 1");
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            },
            new()
            {
                Name = "Failing Hook",
                ContinueOnError = true,
                Execute = _ => throw new Exception("Hook failed")
            },
            new()
            {
                Name = "Working Hook 2",
                ContinueOnError = true,
                Execute = async _ =>
                {
                    executionLog.Add("Working Hook 2");
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
        };

        // Act - Execute hooks with error handling based on ContinueOnError
        foreach (var hook in hooks)
        {
            try
            {
                await hook.Execute(crew);
            }
            catch (Exception)
            {
                if (!hook.ContinueOnError)
                {
                    break; // Stop execution if ContinueOnError is false
                }
                // Continue if ContinueOnError is true
            }
        }

        // Assert
        Assert.Equal(2, executionLog.Count);
        Assert.Contains("Working Hook 1", executionLog);
        Assert.Contains("Working Hook 2", executionLog);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStopAfterException_WhenUsingCrewHooksWithContinueOnErrorFalse()
    {
        // Arrange
        var executionLog = new List<string>();
        var crew = CreateTestCrew();

        var hooks = new List<BeforeKickoffHook>
        {
            new()
            {
                Name = "Working Hook 1",
                ContinueOnError = false,
                Execute = async _ =>
                {
                    executionLog.Add("Working Hook 1");
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            },
            new()
            {
                Name = "Failing Hook",
                ContinueOnError = false,
                Execute = _ => throw new Exception("Hook failed")
            },
            new()
            {
                Name = "Working Hook 2",
                ContinueOnError = false,
                Execute = async _ =>
                {
                    executionLog.Add("Working Hook 2");
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
        };

        // Act - Execute hooks with error handling
        var exceptionThrown = false;
        foreach (var hook in hooks)
        {
            try
            {
                await hook.Execute(crew);
            }
            catch (Exception)
            {
                exceptionThrown = true;
                if (!hook.ContinueOnError)
                {
                    break; // Stop execution
                }
            }
        }

        // Assert
        Assert.True(exceptionThrown);
        Assert.Single(executionLog);
        Assert.Contains("Working Hook 1", executionLog);
        Assert.DoesNotContain("Working Hook 2", executionLog);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldAllowAssignment_WhenUsingCrewHooksWithNullExecuteFunction()
    {
        // Arrange & Act
        var beforeHook = new BeforeKickoffHook { Execute = null! };
        var afterHook = new AfterKickoffHook { Execute = null! };

        // Assert
        Assert.Null(beforeHook.Execute);
        Assert.Null(afterHook.Execute);
    }

    [Fact]
    public void ShouldAcceptAll_WhenUsingCrewHooksWithVeryLongNames()
    {
        // Arrange
        var longName = new string('A', 1000);

        // Act
        var beforeHook = new BeforeKickoffHook { Name = longName };
        var afterHook = new AfterKickoffHook { Name = longName };

        // Assert
        Assert.Equal(longName, beforeHook.Name);
        Assert.Equal(longName, afterHook.Name);
        Assert.Equal(1000, beforeHook.Name.Length);
        Assert.Equal(1000, afterHook.Name.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldWorkCorrectly_WhenUsingCrewHooksWithTaskReturningExecute()
    {
        // Arrange
        var crew = CreateTestCrew();
        var result = "test result";

        var executed1 = false;
        var executed2 = false;

        var beforeHook = new BeforeKickoffHook
        {
            Execute = async _ =>
            {
                await System.Threading.Tasks.Task.Delay(1);
                executed1 = true;
            }
        };

        var afterHook = new AfterKickoffHook
        {
            Execute = async (_, __) =>
            {
                await System.Threading.Tasks.Task.Delay(1);
                executed2 = true;
            }
        };

        // Act
        await beforeHook.Execute(crew);
        await afterHook.Execute(crew, result);

        // Assert
        Assert.True(executed1);
        Assert.True(executed2);
    }

    #endregion
}
