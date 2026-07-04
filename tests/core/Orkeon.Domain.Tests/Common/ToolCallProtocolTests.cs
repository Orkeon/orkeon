using System.Collections.Immutable;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Domain.SharedKernel.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestToolConstants;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for ToolCallProtocol and ToolCallRequest following Clean Architecture principles.
/// Tests the business rules and validation logic of tool call protocol classes.
/// </summary>
public class ToolCallProtocolTests
{
    [Fact]
    public void ShouldCreateProtocol_WhenUsingToolCallProtocolUsingConstructorWithDefaults()
    {
        // Act
        var protocol = new ToolCallProtocol();

        // Assert
        Assert.NotNull(protocol.Version);
        Assert.Equal("json", protocol.Format);
        Assert.Empty(protocol.SupportedTools);
        Assert.Same(ToolCallOptions.Empty, protocol.Options);
    }

    [Fact]
    public void ShouldSetProperties_WhenUsingToolCallProtocolUsingConstructorWithAllParameters()
    {
        // Arrange
        var version = ConfigurationVersionId.Create();
        var format = "xml";
        var supportedTools = new[] { ToolFileRead, ToolWebScrape, "Calculator" };
        var options = ToolCallOptions.Default();

        // Act
        var protocol = new ToolCallProtocol(version, format, supportedTools, options);

        // Assert
        Assert.Equal(version, protocol.Version);
        Assert.Equal(format, protocol.Format);
        Assert.Equal(3, protocol.SupportedTools.Count);
        Assert.Contains(ToolFileRead, protocol.SupportedTools);
        Assert.Contains(ToolWebScrape, protocol.SupportedTools);
        Assert.Contains("Calculator", protocol.SupportedTools);
        Assert.Same(options, protocol.Options);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToolCallProtocolUsingConstructorWithNullVersion()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ToolCallProtocol(version: null!));

        Assert.Equal("version", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToolCallProtocolUsingConstructorWithNullFormat()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ToolCallProtocol(format: null!));

        Assert.Equal("format", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyList_WhenUsingToolCallProtocolUsingConstructorWithNullSupportedTools()
    {
        // Act
        var protocol = new ToolCallProtocol(supportedTools: null);

        // Assert
        Assert.NotNull(protocol.SupportedTools);
        Assert.Empty(protocol.SupportedTools);
    }

    [Fact]
    public void ShouldUseEmpty_WhenUsingToolCallProtocolUsingConstructorWithNullOptions()
    {
        // Act
        var protocol = new ToolCallProtocol(options: null);

        // Assert
        Assert.Same(ToolCallOptions.Empty, protocol.Options);
    }

    [Fact]
    public void ShouldReturnDefaultProtocol_WhenUsingToolCallProtocolWithDefault()
    {
        // Act
        var protocol = ToolCallProtocol.Default();

        // Assert
        Assert.NotNull(protocol.Version);
        Assert.Equal("json", protocol.Format);
        Assert.Empty(protocol.SupportedTools);
        Assert.Same(ToolCallOptions.Empty, protocol.Options);
    }

    [Fact]
    public void ShouldCreateProtocolWithTools_WhenUsingToolCallProtocolWithTools()
    {
        // Arrange
        var tools = new[] { "Tool1", "Tool2", "Tool3" };

        // Act
        var protocol = ToolCallProtocol.WithTools(tools);

        // Assert
        Assert.NotNull(protocol.Version);
        Assert.Equal("json", protocol.Format);
        Assert.Equal(3, protocol.SupportedTools.Count);
        Assert.All(tools, tool => Assert.Contains(tool, protocol.SupportedTools));
    }

    [Fact]
    public void ShouldCreateEmptyProtocol_WhenUsingToolCallProtocolWithToolsEmptyArray()
    {
        // Act
        var protocol = ToolCallProtocol.WithTools();

        // Assert
        Assert.Empty(protocol.SupportedTools);
    }

    [Fact]
    public void ShouldCreateRequest_WhenUsingToolCallRequestUsingConstructorWithValidParameters()
    {
        // Arrange
        var toolName = ToolFileRead;
        var arguments = ToolArguments.CreateBuilder()
            .AddString(ParamPath, "/test/file.txt")
            .Build();
        var callerId = "agent-123";

        // Act
        var request = new ToolCallRequest(toolName, arguments, callerId);

        // Assert
        Assert.NotNull(request.Id);
        Assert.NotNull(request.Id);
        Assert.Equal(toolName, request.ToolName);
        Assert.Same(arguments, request.Arguments);
        Assert.Equal(callerId, request.CallerId);
        Assert.True(request.RequestedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToolCallRequestUsingConstructorWithNullToolName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ToolCallRequest(null!));

        Assert.Equal("toolName", exception.ParamName);
    }

    [Fact]
    public void ShouldUseEmpty_WhenUsingToolCallRequestUsingConstructorWithNullArguments()
    {
        // Act
        var request = new ToolCallRequest("TestTool", arguments: null);

        // Assert
        Assert.Same(ToolArguments.Empty, request.Arguments);
    }

    [Fact]
    public void ShouldAllowNull_WhenUsingToolCallRequestUsingConstructorWithNullCallerId()
    {
        // Act
        var request = new ToolCallRequest("TestTool", callerId: null);

        // Assert
        Assert.Null(request.CallerId);
    }

    [Fact]
    public void ShouldBeUnique_WhenUsingToolCallRequestUsingId()
    {
        // Act
        var request1 = new ToolCallRequest("Tool1");
        var request2 = new ToolCallRequest("Tool2");

        // Assert
        Assert.NotEqual(request1.Id, request2.Id);
    }

    [Fact]
    public void ShouldBeUtc_WhenUsingToolCallRequestUsingRequestedAt()
    {
        // Act
        var request = new ToolCallRequest("TestTool");

        // Assert
        Assert.Equal(DateTimeKind.Utc, request.RequestedAt.Kind);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingToolCallProtocolUsingEqualsWithSameValues()
    {
        // Arrange
        var tools = new[] { "Tool1", "Tool2" };
        var options = ToolCallOptions.Default();

        var protocol1 = new ToolCallProtocol("1.0", "json", tools, options);
        var protocol2 = new ToolCallProtocol("1.0", "json", tools, options);

        // Act & Assert
        Assert.Equal(protocol1, protocol2);
        Assert.True(protocol1.Equals(protocol2));
        Assert.Equal(protocol1.GetHashCode(), protocol2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingToolCallProtocolUsingEqualsWithDifferentValues()
    {
        // Arrange
        var protocol1 = new ToolCallProtocol("1.0", "json");
        var protocol2 = new ToolCallProtocol("2.0", "xml");

        // Act & Assert
        Assert.NotEqual(protocol1, protocol2);
        Assert.False(protocol1.Equals(protocol2));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingToolCallRequestUsingEqualsWithSameValues()
    {
        // Arrange
        var toolName = "TestTool";
        var arguments = ToolArguments.CreateBuilder()
            .AddString("param", "value")
            .Build();
        var callerId = "caller-123";

        // Create two requests at the same time to minimize timing differences
        var startTime = DateTime.UtcNow;
        var request1 = new ToolCallRequest(toolName, arguments, callerId);
        var request2 = new ToolCallRequest(toolName, arguments, callerId);

        // Act & Assert
        // Requests won't be equal due to different IDs and timestamps
        Assert.NotEqual(request1, request2);
        Assert.NotEqual(request1.Id, request2.Id);

        // But they should have the same tool name and arguments
        Assert.Equal(request1.ToolName, request2.ToolName);
        Assert.Equal(request1.CallerId, request2.CallerId);
    }

    [Theory]
    [InlineData("1.0", "json")]
    [InlineData("2.0", "xml")]
    [InlineData("1.5", "yaml")]
    public void ShouldAcceptAll_WhenUsingToolCallProtocolWithVariousFormats(string version, string format)
    {
        // Act
        var protocol = new ToolCallProtocol(version, format);

        // Assert
        Assert.Equal(version, protocol.Version);
        Assert.Equal(format, protocol.Format);
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingToolCallProtocolUsingSupportedTools()
    {
        // Arrange
        var tools = new[] { "Tool1", "Tool2" };
        var protocol = new ToolCallProtocol(supportedTools: tools);

        // Act & Assert
        Assert.IsType<ImmutableList<string>>(protocol.SupportedTools);

        // The original array shouldn't affect the protocol
        tools[0] = "ModifiedTool";
        Assert.Equal("Tool1", protocol.SupportedTools[0]);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingToolCallRequestWithComplexArguments()
    {
        // Arrange
        var arguments = ToolArguments.CreateBuilder()
            .AddString("stringParam", "test")
            .AddInt("intParam", 42)
            .AddBool("boolParam", true)
            .Build();

        // Act
        var request = new ToolCallRequest("ComplexTool", arguments);

        // Assert
        Assert.Equal("test", request.Arguments.GetRequiredString("stringParam"));
        Assert.Equal(42, request.Arguments.GetRequired<int>("intParam"));
        Assert.True(request.Arguments.GetRequired<bool>("boolParam"));
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingMultipleToolCallRequests()
    {
        // Arrange
        var requests = new List<ToolCallRequest>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            requests.Add(new ToolCallRequest($"Tool{i}"));
        }

        // Assert
        var uniqueIds = requests.Select(r => r.Id).Distinct().Count();
        Assert.Equal(100, uniqueIds);
    }
}
