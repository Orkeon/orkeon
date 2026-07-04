using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;

namespace Orkeon.Domain.Tests.Task;

/// <summary>
/// LLM Response Format — task-level override surface coverage.
/// Verifies the new <c>CrewTask.LlmOverride</c> property is null by default,
/// settable via <c>SetLlmOverride</c>, and reachable through the fluent
/// <see cref="CrewTaskBuilder.WithLlmOverride"/> / <c>WithResponseFormat</c> shortcuts.
/// </summary>
public class CrewTaskLlmOverrideTests
{
    private static CrewTaskBuilder MinimalTask() =>
        new CrewTaskBuilder().Description("Test").ExpectedOutput("Expected");

    [Fact]
    public void LlmOverride_IsNull_WhenTaskCreatedWithoutOverride()
    {
        var task = MinimalTask().Build();
        Assert.Null(task.LlmOverride);
    }

    [Fact]
    public void SetLlmOverride_AssignsValue_WhenCalledBeforeStart()
    {
        var task = MinimalTask().Build();
        var ov = LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject());

        task.SetLlmOverride(ov);

        Assert.NotNull(task.LlmOverride);
        Assert.Equal("json_object", task.LlmOverride!.ResponseFormat!.Type);
    }

    [Fact]
    public void SetLlmOverride_AcceptsNull_ToClear()
    {
        var task = MinimalTask().Build();
        task.SetLlmOverride(LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject()));
        task.SetLlmOverride(null);
        Assert.Null(task.LlmOverride);
    }

    [Fact]
    public void Builder_WithLlmOverride_PropagatesToTask()
    {
        var ov = new LlmConfigOverride
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
            Temperature = 0.0,
        };

        var task = MinimalTask().WithLlmOverride(ov).Build();

        Assert.NotNull(task.LlmOverride);
        Assert.Equal("json_object", task.LlmOverride!.ResponseFormat!.Type);
        Assert.Equal(0.0, task.LlmOverride.Temperature);
    }

    [Fact]
    public void Builder_WithResponseFormat_String_BuildsTaskWithJsonObjectOverride()
    {
        var task = MinimalTask().WithResponseFormat("json_object").Build();

        Assert.NotNull(task.LlmOverride);
        Assert.NotNull(task.LlmOverride!.ResponseFormat);
        Assert.Equal("json_object", task.LlmOverride.ResponseFormat!.Type);
    }

    [Fact]
    public void Builder_WithResponseFormat_Typed_BuildsTaskWithJsonObjectOverride()
    {
        var task = MinimalTask().WithResponseFormat(LlmResponseFormat.JsonObject()).Build();

        Assert.Equal("json_object", task.LlmOverride!.ResponseFormat!.Type);
    }

    [Fact]
    public void Builder_WithResponseFormat_DoesNotEraseExistingOverride_WhenChained()
    {
        var task = MinimalTask()
            .WithLlmOverride(new LlmConfigOverride { Temperature = 0.0 })
            .WithResponseFormat("json_object")
            .Build();

        Assert.Equal(0.0, task.LlmOverride!.Temperature);
        Assert.Equal("json_object", task.LlmOverride.ResponseFormat!.Type);
    }
}
