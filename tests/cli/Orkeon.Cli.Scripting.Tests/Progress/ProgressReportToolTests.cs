using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Cli.Scripting.Progress;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Cli.Scripting.Tests.Progress;

public sealed class ProgressReportToolTests
{
    private static Task<ToolCallResponse> CallAsync(ProgressReportTool tool, Dictionary<string, object?> args)
        => tool.CallAsync(new ToolCallRequest(tool.Name, args));

    [Fact]
    public async Task Report_publishes_to_the_broker()
    {
        var broker = new ProgressBroker();
        using var tool = new ProgressReportTool(broker);

        var resp = await CallAsync(tool, new()
        {
            ["label"] = "Compacting conversation",
            ["step"] = 2,
            ["total"] = 5,
            ["message"] = "synthesis",
        });

        Assert.True(resp.Success, resp.Error);
        var current = broker.Current;
        Assert.NotNull(current);
        Assert.Equal("Compacting conversation", current!.Label);
        Assert.Equal(2, current.Step);
        Assert.Equal(5, current.Total);
        Assert.Equal("synthesis", current.Message);
    }

    [Fact]
    public async Task Done_clears_the_matching_label_only()
    {
        var broker = new ProgressBroker();
        using var tool = new ProgressReportTool(broker);
        await CallAsync(tool, new() { ["label"] = "Compacting conversation", ["step"] = 1, ["total"] = 2 });

        await CallAsync(tool, new() { ["label"] = "another operation", ["done"] = true });
        Assert.NotNull(broker.Current);

        await CallAsync(tool, new() { ["label"] = "Compacting conversation", ["done"] = true });
        Assert.Null(broker.Current);
    }

    [Fact]
    public async Task Blank_label_degrades_to_a_generic_headline()
    {
        var broker = new ProgressBroker();
        using var tool = new ProgressReportTool(broker);

        var resp = await CallAsync(tool, new() { ["label"] = "  ", ["percent"] = 50.0 });

        Assert.True(resp.Success);
        Assert.Equal("Working", broker.Current!.Label);
    }

    [Fact]
    public async Task Ambient_instance_receives_the_progress_snapshot()
    {
        // The crew body runs inside detached postWork; the tool must stamp the owning
        // instance so ps/inspect and the agents pane see the same progress.
        var broker = new ProgressBroker();
        using var tool = new ProgressReportTool(broker);
        var registry = new CommandInstanceRegistry();
        var instance = registry.Register("compact", CommandInstanceKind.Async, "crew:session-compact", "compact", Guid.NewGuid());

        ProgressAmbient.CurrentInstance = instance;
        try
        {
            await CallAsync(tool, new() { ["label"] = "Compacting conversation", ["percent"] = 40.0, ["message"] = "snip" });
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
        }

        var view = instance.Snapshot();
        Assert.NotNull(view.progress);
        Assert.Equal(40.0, view.progress!.percent);
        Assert.Equal("snip", view.progress.message);
        Assert.Equal(instance.Ticket, broker.Current!.Ticket);
    }
}
