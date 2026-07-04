using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.HumanInput;
using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.Tools.HumanInput;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Infrastructure.Tests.Tools.HumanInputTool;

public class HumanInputToolTests
{
    private sealed class StubProvider : IHumanInputProvider
    {
        public string TextReply { get; set; } = "";
        public bool ConfirmReply { get; set; }
        public string ChoiceReply { get; set; } = "";
        public HumanInputContext? LastContext { get; private set; }

        public Task<string> GetInputAsync(HumanInputContext context, CancellationToken ct = default)
        { LastContext = context; return Task.FromResult(TextReply); }

        public Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken ct = default)
        { LastContext = context; return Task.FromResult(ConfirmReply); }

        public Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken ct = default)
        { LastContext = context; return Task.FromResult(ChoiceReply); }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    private static HumanInputResponse Decode(object? raw)
    {
        var dict = (Dictionary<string, object?>)raw!;
        return new HumanInputResponse
        {
            Response = dict["response"]!.ToString()!,
            InputType = dict["input_type"]!.ToString()!,
            IsApproved = Convert.ToBoolean(dict["is_approved"]),
            WasDefault = Convert.ToBoolean(dict["was_default"]),
        };
    }

    [Fact]
    public async Task TextInput_ReturnsProviderText_AndDispatchesToGetInputAsync()
    {
        var provider = new StubProvider { TextReply = "the user said this" };
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "Tell me something",
            ["input_type"] = "text",
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var resp = Decode(result.Result);
        Assert.Equal("the user said this", resp.Response);
        Assert.Equal("text", resp.InputType);
        Assert.False(resp.IsApproved);
        Assert.False(resp.WasDefault);
        Assert.NotNull(provider.LastContext);
        Assert.Equal("Tell me something", provider.LastContext!.Prompt);
    }

    [Fact]
    public async Task ApprovalInput_RoutesToGetConfirmationAsync_AndYieldsApproved()
    {
        var provider = new StubProvider { ConfirmReply = true };
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "OK to proceed?",
            ["input_type"] = "approval",
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var resp = Decode(result.Result);
        Assert.Equal("approved", resp.Response);
        Assert.True(resp.IsApproved);
    }

    [Fact]
    public async Task ApprovalInput_RoutesToGetConfirmationAsync_AndYieldsRejected()
    {
        var provider = new StubProvider { ConfirmReply = false };
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "OK?",
            ["input_type"] = "approval",
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var resp = Decode(result.Result);
        Assert.Equal("rejected", resp.Response);
        Assert.False(resp.IsApproved);
    }

    [Fact]
    public async Task ChoiceInput_RoutesToGetChoiceAsync_AndDetectsApproval()
    {
        var provider = new StubProvider { ChoiceReply = "approved" };
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "Validate the CR",
            ["input_type"] = "choice",
            ["choices"] = new List<string> { "approved", "rejected", "edit_then_approve" },
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var resp = Decode(result.Result);
        Assert.Equal("approved", resp.Response);
        Assert.True(resp.IsApproved);
        Assert.NotNull(provider.LastContext);
        Assert.Equal(3, provider.LastContext!.Options.Count);
    }

    [Fact]
    public async Task ChoiceInput_WithEmptyChoicesList_Fails()
    {
        var provider = new StubProvider();
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "Pick one",
            ["input_type"] = "choice",
            ["choices"] = new List<string>(),
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("choices", result.Error ?? "");
    }

    [Fact]
    public async Task EmptyPrompt_Fails()
    {
        var provider = new StubProvider();
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "",
            ["input_type"] = "text",
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task UnknownInputType_Fails()
    {
        var provider = new StubProvider();
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "Hello",
            ["input_type"] = "telepathy",
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task EditFilePath_IsForwardedTo_HumanInputContextMetadata()
    {
        var provider = new StubProvider { ChoiceReply = "edit_then_approve" };
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "Validate the CR",
            ["input_type"] = "choice",
            ["choices"] = new List<string> { "approved", "rejected", "edit_then_approve" },
            ["edit_file_path"] = "/output/report.md",
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(provider.LastContext);
        Assert.True(provider.LastContext!.Metadata.TryGetValue(
            HumanInputDefaults.EditFilePathMetadataKey, out var pathObj));
        Assert.Equal("/output/report.md", pathObj as string);
    }

    [Fact]
    public async Task EditFilePath_Absent_LeavesMetadataEmpty()
    {
        var provider = new StubProvider { ChoiceReply = "approved" };
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "Validate the CR",
            ["input_type"] = "choice",
            ["choices"] = new List<string> { "approved", "rejected" },
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(provider.LastContext);
        Assert.False(provider.LastContext!.Metadata.ContainsKey(
            HumanInputDefaults.EditFilePathMetadataKey));
    }

    [Fact]
    public async Task DefaultValue_FlagsWasDefault_WhenProviderReturnsIt()
    {
        var provider = new StubProvider { TextReply = "fallback" };
        using var tool = new Infrastructure.Tools.HumanInput.HumanInputTool(provider);
        var req = new ProtocolToolCallRequest("human_input", new Dictionary<string, object?>
        {
            ["prompt"] = "?",
            ["input_type"] = "text",
            ["default_value"] = "fallback",
        });

        var result = await tool.CallAsync(req, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var resp = Decode(result.Result);
        Assert.True(resp.WasDefault);
    }
}
