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

    [Fact]
    public void A_secret_written_in_the_environment_block_is_reported_as_information_unless_it_names_a_variable()
    {
        var clear = Validate("""{ "MCP": { "Servers": { "s": { "Command": "x", "Env": { "GITHUB_TOKEN": "ghp_abc", "NODE_ENV": "production" } } } } }""");
        var named = Validate("""{ "MCP": { "Servers": { "s": { "Command": "x", "Env": { "GITHUB_TOKEN": "${GITHUB_TOKEN}" } } } } }""");

        var message = Assert.Single(clear, m => m.Code == ValidationCodes.McpEnvLooksSecret);
        Assert.Equal(ValidationSeverity.Information, message.Severity);
        Assert.Equal("MCP:Servers:s:Env:GITHUB_TOKEN", message.Path);
        Assert.Null(Find(named, ValidationCodes.McpEnvLooksSecret));
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
