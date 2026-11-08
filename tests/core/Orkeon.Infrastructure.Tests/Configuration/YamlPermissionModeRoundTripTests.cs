using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Security;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>FWK-06: the <c>permissionMode</c> key of a YAML agent survives the import and the export.</summary>
public sealed class YamlPermissionModeRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string researcherLines) => $$"""
        name: gated
        goal: Answer
        process: sequential
        agents:
          researcher:
            role: Researcher
            goal: Find the facts
        {{researcherLines}}
        tasks:
          find:
            description: Find the facts
            expected_output: Facts
            agent: researcher
        """;

    private static YamlCrewDefinitionLoader NewLoader() =>
        new(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Theory]
    [InlineData("plan", PermissionMode.Plan)]
    [InlineData("acceptEdits", PermissionMode.AcceptEdits)]
    [InlineData("dontAsk", PermissionMode.DontAsk)]
    public async Task ShouldReachTheConfiguration_WhenYamlDeclaresPermissionMode(string written, PermissionMode expected)
    {
        var config = await NewLoader().LoadFromStringAsync(Yaml($"    permissionMode: {written}"), Ct);

        Assert.Equal(expected, config.Agents.Single().PermissionMode);
    }

    [Fact]
    public async Task ShouldLeaveItNull_WhenYamlDeclaresNone()
    {
        var config = await NewLoader().LoadFromStringAsync(Yaml("    allowDelegation: false"), Ct);

        Assert.Null(config.Agents.Single().PermissionMode);
    }

    [Fact]
    public async Task ShouldRefuseTheLoad_WhenTheModeIsUnknown()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewLoader().LoadFromStringAsync(Yaml("    permissionMode: yolo"), Ct));

        Assert.Contains("yolo", ex.Message, StringComparison.Ordinal);
        Assert.Contains("permissionMode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSurviveTheExport()
    {
        var agentId = AgentId.Create();
        var original = new CrewConfiguration
        {
            Name = "gated",
            Goal = "Answer",
            Process = ProcessType.Sequential,
            Agents =
            [
                new AgentConfiguration { Id = agentId, Role = "Researcher", Goal = "Find", PermissionMode = PermissionMode.BypassPermissions },
            ],
            Tasks =
            [
                new TaskConfiguration { Id = TaskId.Create(), Description = "Find", ExpectedOutput = "Facts", AssignedAgentId = agentId },
            ],
        };
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewExporter>.Instance);

        var yaml = exporter.ExportToString(original);
        var reloaded = await NewLoader().LoadFromStringAsync(yaml, Ct);

        Assert.Equal(PermissionMode.BypassPermissions, reloaded.Agents.Single().PermissionMode);
    }
}
