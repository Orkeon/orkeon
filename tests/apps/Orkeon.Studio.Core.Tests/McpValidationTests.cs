using Orkeon.Constants.Configuration;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>STUDIO-21 — the MCP rules the runtime applies at connection, said before the save.</summary>
public sealed class McpValidationTests
{
    private static IReadOnlyList<ValidationMessage> Validate(string json) =>
        new AppSettingsValidator(new FakeDirectoryProbe()).Validate(AppSettingsDocument.Parse(json));

    private static ValidationMessage? Find(IEnumerable<ValidationMessage> messages, string code) =>
        messages.FirstOrDefault(m => m.Code == code);

    [Fact]
    public void A_stdio_server_without_a_command_is_an_error_on_its_command_key()
    {
        var message = Find(Validate("""{ "MCP": { "Servers": { "files": { "Args": ["-y"] } } } }"""), ValidationCodes.McpCommandMissing);

        Assert.NotNull(message);
        Assert.Equal(ValidationSeverity.Error, message.Severity);
        Assert.Equal("MCP:Servers:files:Command", message.Path);
    }

    [Fact]
    public void An_http_server_needs_an_absolute_http_url()
    {
        var bad = Validate("""{ "MCP": { "Servers": { "r": { "Transport": "Sse", "Url": "mcp.example.com" } } } }""");
        var good = Validate("""{ "MCP": { "Servers": { "r": { "Transport": "sse", "Url": "https://mcp.example.com/rpc" } } } }""");

        Assert.Equal("MCP:Servers:r:Url", Find(bad, ValidationCodes.McpUrlInvalid)!.Path);
        Assert.Null(Find(good, ValidationCodes.McpUrlInvalid));
        Assert.Null(Find(good, ValidationCodes.McpCommandMissing));
    }

    [Fact]
    public void An_unknown_transport_and_a_bad_identifier_are_errors()
    {
        var messages = Validate("""{ "MCP": { "Servers": { "my server": { "Transport": "grpc", "Command": "x" } } } }""");

        Assert.Equal("MCP:Servers:my server", Find(messages, ValidationCodes.McpServerIdInvalid)!.Path);
        Assert.Contains("grpc", Find(messages, ValidationCodes.McpTransportUnknown)!.Text, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-15")]
    [InlineData("${GITHUB_TOKEN}")]
    [InlineData("%APPDATA%")]
    public void ShouldReportAnError_WhenAnMcpEnvEntryIsAReference(string value)
    {
        const string path = "MCP:Servers:s:Env:GITHUB_TOKEN";
        var json = $$"""{ "MCP": { "Servers": { "s": { "Command": "x", "Env": { "GITHUB_TOKEN": "{{value}}" } } } } }""";

        var messages = Validate(json).Where(m => m.Path == path).ToList();

        var error = Assert.Single(messages);
        Assert.Equal(ValidationSeverity.Error, error.Severity);
        Assert.Contains(
            OperatorMessages.SettingPlaceholderRefused(path, SettingPlaceholders.CorrectionFor(path)!),
            error.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(messages, m => m.Code == ValidationCodes.McpEnvLooksSecret);
    }

    [Theory]
    [Trait("BR", "CH-15")]
    [InlineData("ghp_abc")]
    [InlineData("env:X")]
    [InlineData("ORKEON_X")]
    public void ShouldReportThePlainTextInformation_WhenAnEnvValueLooksLikeASecret(string value)
    {
        var messages = Validate($$"""{ "MCP": { "Servers": { "s": { "Command": "x", "Env": { "GITHUB_TOKEN": "{{value}}", "NODE_ENV": "production" } } } } }""");

        var message = Assert.Single(messages, m => m.Code == ValidationCodes.McpEnvLooksSecret);
        Assert.Equal(ValidationSeverity.Information, message.Severity);
        Assert.Equal("MCP:Servers:s:Env:GITHUB_TOKEN", message.Path);
    }

    [Fact]
    public void A_well_formed_section_and_the_shell_switch_raise_nothing_of_their_own()
    {
        var messages = Validate("""
            { "Llm": { "Model": "m", "BaseUrl": "http://localhost:11434" },
              "Orkeon": { "FileSystem": { "Mounts": ["/tmp:/data:ro"] }, "Tools": { "Shell": { "AllowInterpreters": "yes" } } },
              "MCP": { "Enabled": true, "Servers": { "files": { "Command": "npx", "Args": ["-y"] } } } }
            """);

        Assert.DoesNotContain(messages, m => m.Code.StartsWith("STUDIO-MCP", StringComparison.Ordinal));
        var wrongType = Assert.Single(messages, m => m.Code == ValidationCodes.InvalidFieldType);
        Assert.Equal("Orkeon:Tools:Shell:AllowInterpreters", wrongType.Path);
    }
}
