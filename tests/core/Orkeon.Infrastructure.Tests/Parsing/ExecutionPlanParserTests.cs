using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Parsing;

namespace Orkeon.Infrastructure.Tests.Parsing;

/// <summary>
/// GAP-31: the reader of the crew planner's reply — <c>{"plans": [{"task": 1, "plan": "…"}]}</c>, one
/// step-by-step plan per task, by the number the planning prompt gave it. Tolerant of what models
/// wrap around JSON (a fence, a sentence); a number given twice keeps its first plan, a number the
/// crew does not have is ignored, each with a warning; a reply without a <c>plans</c> array cannot be
/// read, and says why. The former hybrid reader (GUIDs to copy back, an order, an agent, parallel
/// groups, dependencies and a <c>KEY: value</c> fallback) is gone with what it fed.
/// </summary>
public class ExecutionPlanParserTests
{
    private readonly ExecutionPlanParser _parser = new();

    private static List<TaskId> Tasks(int count) => [.. Enumerable.Range(0, count).Select(_ => TaskId.Create())];

    [Fact]
    public void Reads_one_plan_per_task_by_number()
    {
        var tasks = Tasks(2);

        var reading = _parser.Read("""{"plans":[{"task":2,"plan":"1. Write."},{"task":1,"plan":"1. Outline.\n2. Check."}]}""", tasks);

        Assert.Null(reading.Error);
        Assert.Equal("1. Outline.\n2. Check.", reading.Plan!.InstructionsFor(tasks[0]));
        Assert.Equal("1. Write.", reading.Plan.InstructionsFor(tasks[1]));
        Assert.Empty(reading.Warnings);
    }

    [Theory]
    [InlineData("```json\n{\"plans\":[{\"task\":1,\"plan\":\"A\"}]}\n```")]
    [InlineData("Here is the plan you asked for: {\"plans\":[{\"task\":1,\"plan\":\"A\"}]} Let me know.")]
    [InlineData("{\"Plans\":[{\"Task\":1,\"Plan\":\"A\"}]}")]
    [InlineData("{\"plans\":[{\"task\":\"1\",\"plan\":\"A\"}]}")]
    public void Reads_what_models_wrap_around_the_object(string reply)
    {
        var tasks = Tasks(1);

        var reading = _parser.Read(reply, tasks);

        Assert.Equal("A", reading.Plan!.InstructionsFor(tasks[0]));
    }

    [Fact]
    public void Reads_a_plan_written_as_a_list_of_steps()
    {
        var tasks = Tasks(1);

        var reading = _parser.Read("""{"plans":[{"task":1,"plan":["1. Outline.","2. Check."]}]}""", tasks);

        Assert.Equal("1. Outline.\n2. Check.", reading.Plan!.InstructionsFor(tasks[0]));
    }

    [Fact]
    public void A_number_given_twice_keeps_its_first_plan_and_warns()
    {
        var tasks = Tasks(1);

        var reading = _parser.Read("""{"plans":[{"task":1,"plan":"FIRST"},{"task":1,"plan":"SECOND"}]}""", tasks);

        Assert.Equal("FIRST", reading.Plan!.InstructionsFor(tasks[0]));
        Assert.Single(reading.Plan.Tasks);
        var warning = Assert.Single(reading.Warnings);
        Assert.Contains("task 1", warning, StringComparison.Ordinal);
        Assert.Contains("first", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void A_number_the_crew_does_not_have_is_ignored_with_a_warning(int number)
    {
        var tasks = Tasks(2);

        var reading = _parser.Read($$"""{"plans":[{"task":{{number}},"plan":"X"},{"task":1,"plan":"A"}]}""", tasks);

        Assert.Equal("A", reading.Plan!.InstructionsFor(tasks[0]));
        Assert.Single(reading.Plan.Tasks);
        Assert.Contains($"task {number}", Assert.Single(reading.Warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"plans":[{"plan":"no number"},{"task":1,"plan":"A"}]}""")]
    [InlineData("""{"plans":[{"task":"first","plan":"not a number"},{"task":1,"plan":"A"}]}""")]
    [InlineData("""{"plans":[{"task":2},{"task":1,"plan":"A"}]}""")]
    [InlineData("""{"plans":[{"task":2,"plan":"   "},{"task":1,"plan":"A"}]}""")]
    [InlineData("""{"plans":["a string",{"task":1,"plan":"A"}]}""")]
    public void An_entry_without_a_number_or_a_plan_is_ignored_with_a_warning(string reply)
    {
        var tasks = Tasks(2);

        var reading = _parser.Read(reply, tasks);

        Assert.Null(reading.Error);
        Assert.Equal("A", reading.Plan!.InstructionsFor(tasks[0]));
        Assert.Null(reading.Plan.InstructionsFor(tasks[1]));
        Assert.Single(reading.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("I would start with the outline, then write the draft.")]
    [InlineData("{}")]
    [InlineData("""{"plans":{"task":1,"plan":"A"}}""")]
    [InlineData("""{"tasks":[{"task":1,"order":1}]}""")]
    [InlineData("""{"plans":[{"task":1,"plan":"cut in the mi""")]
    public void A_reply_without_a_plans_array_cannot_be_read_and_says_why(string reply)
    {
        var reading = _parser.Read(reply, Tasks(1));

        Assert.Null(reading.Plan);
        Assert.False(string.IsNullOrWhiteSpace(reading.Error));
    }

    [Fact]
    public void GuardsItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _parser.Read(null!, Tasks(1)));
        Assert.Throws<ArgumentNullException>(() => _parser.Read("{}", null!));
    }
}
