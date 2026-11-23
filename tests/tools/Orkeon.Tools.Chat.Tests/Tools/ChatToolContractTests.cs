using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Chat.DependencyInjection;
using Orkeon.Tools.Chat.Tests.Fixtures;

namespace Orkeon.Tools.Chat.Tests.Tools;

/// <summary>
/// What every chat tool declares to the agent loop: its name, access class and category, which
/// parameters are required, and a return schema that keeps every field of its response (the
/// output filter drops any field the schema does not declare).
/// </summary>
public sealed class ChatToolContractTests
{
    [Theory]
    [InlineData("chat_accounts", "Read")]
    [InlineData("chat_list", "Read")]
    [InlineData("chat_read", "Read")]
    [InlineData("chat_members", "Read")]
    [InlineData("chat_search", "Read")]
    [InlineData("chat_send", "Execute")]
    [InlineData("chat_edit", "Execute")]
    [InlineData("chat_delete", "Execute")]
    [InlineData("chat_react", "Execute")]
    public void Should_declare_its_name_access_and_category(string name, string access)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());

        var tool = fixture.Tools[name];

        Assert.Equal(name, tool.Name);
        Assert.Equal(Enum.Parse<ToolAccess>(access), tool.Access);
        Assert.Equal("Chat", tool.Category);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Equal(name, tool.Schema.Name);
    }

    [Fact]
    public void Should_declare_every_response_field_in_the_return_schema()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());

        foreach (var tool in fixture.Tools.Values)
        {
            var declared = JsonNames(ResponseType(tool));
            var returns = tool.Schema.Returns?.Keys.ToList() ?? [];

            Assert.True(declared.SetEquals(returns),
                $"{tool.Name}: fields [{string.Join(", ", declared)}] vs return schema [{string.Join(", ", returns)}]");
        }
    }

    [Fact]
    public void Should_describe_nested_fields_under_the_names_the_payload_uses()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());
        var dtos = typeof(ChatToolsServiceCollectionExtensions).Assembly.GetTypes()
            .Where(type => type.Namespace == "Orkeon.Tools.Chat.Dtos" && !type.IsNested)
            .ToDictionary(type => JsonNamingPolicy.SnakeCaseLower.ConvertName(type.Name), StringComparer.Ordinal);
        var checkedTypes = 0;

        foreach (var tool in fixture.Tools.Values)
        {
            foreach (var (typeName, definition) in tool.Schema.Types ?? [])
            {
                var properties = Assert.IsType<Dictionary<string, object>>(Assert.IsType<Dictionary<string, object>>(definition)["properties"]);
                var described = properties.Keys.ToHashSet(StringComparer.Ordinal);
                var sent = JsonNames(dtos[typeName]);
                Assert.True(described.SetEquals(sent),
                    $"{tool.Name} / {typeName}: schema [{string.Join(", ", described)}] vs payload [{string.Join(", ", sent)}]");
                checkedTypes++;
            }
        }

        Assert.True(checkedTypes >= 3, $"Only {checkedTypes} nested types were checked.");
    }

    [Theory]
    [InlineData("chat_accounts", "")]
    [InlineData("chat_list", "")]
    [InlineData("chat_read", "place")]
    [InlineData("chat_members", "")]
    [InlineData("chat_search", "")]
    [InlineData("chat_send", "place,text")]
    [InlineData("chat_edit", "place,message_id,text")]
    [InlineData("chat_delete", "place,message_id")]
    [InlineData("chat_react", "place,message_id,reaction")]
    public void Should_require_only_what_the_tool_cannot_do_without(string name, string required)
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());

        var parameters = fixture.Tools[name].Schema.Parameters;

        Assert.Equal(
            required.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal),
            parameters.Where(p => p.Value.Required).Select(p => p.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Should_describe_the_optional_value_parameters_with_their_types()
    {
        using var fixture = new ChatFixture(TestChatAccounts.Teams());

        Assert.Equal("integer", fixture.Tools["chat_list"].Schema.Parameters["limit"].Type);
        Assert.Equal("array", fixture.Tools["chat_send"].Schema.Parameters["mentions"].Type);
        Assert.False(fixture.Tools["chat_read"].Schema.Parameters["since"].Required);
    }

    private static Type ResponseType(ToolBase tool)
    {
        var type = tool.GetType();
        while (type is not null && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ToolBase<,>)))
            type = type.BaseType;
        return type?.GetGenericArguments()[1] ?? throw new InvalidOperationException($"{tool.Name} is not a typed tool.");
    }

    private static HashSet<string> JsonNames(Type response) =>
        response.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name)
            .ToHashSet(StringComparer.Ordinal);
}
