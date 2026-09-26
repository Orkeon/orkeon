using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Email.DependencyInjection;
using Orkeon.Tools.Email.Tests.Fixtures;

namespace Orkeon.Tools.Email.Tests.Tools;

/// <summary>
/// What every e-mail tool declares to the agent loop: its name, access class and category, which
/// parameters are required, and a return schema that keeps every field of its response (the
/// output filter drops any field the schema does not declare).
/// </summary>
public sealed class EmailToolContractTests
{
    [Theory]
    [InlineData("email_accounts", "Read")]
    [InlineData("email_folders", "Read")]
    [InlineData("email_search", "Read")]
    [InlineData("email_read", "Read")]
    [InlineData("email_parser", "Read")]
    [InlineData("email_save_attachment", "Edit")]
    [InlineData("email_create_folder", "Edit")]
    [InlineData("email_rename_folder", "Edit")]
    [InlineData("email_move", "Edit")]
    [InlineData("email_mark", "Edit")]
    [InlineData("email_draft", "Edit")]
    [InlineData("email_delete", "Execute")]
    [InlineData("email_send", "Execute")]
    public void Should_declare_its_name_access_and_category(string name, string access)
    {
        using var fixture = new ToolFixture();

        var tool = fixture.Tools[name];

        Assert.Equal(name, tool.Name);
        Assert.Equal(Enum.Parse<ToolAccess>(access), tool.Access);
        Assert.Equal("Email", tool.Category);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Equal(name, tool.Schema.Name);
    }

    [Fact]
    public void Should_declare_every_response_field_in_the_return_schema()
    {
        using var fixture = new ToolFixture();

        foreach (var tool in fixture.Tools.Values)
        {
            var declared = JsonNames(ResponseType(tool));
            var returns = tool.Schema.Returns?.Keys.ToList() ?? [];

            Assert.True(declared.SetEquals(returns),
                $"{tool.Name}: fields [{string.Join(", ", declared)}] vs return schema [{string.Join(", ", returns)}]");
        }
    }

    /// <summary>
    /// The schema describes a nested DTO under its C# property names, the payload uses the JSON
    /// names: the two must be the same, or the agent reads about a field it never receives.
    /// </summary>
    [Fact]
    public void Should_describe_nested_fields_under_the_names_the_payload_uses()
    {
        using var fixture = new ToolFixture();
        var dtos = typeof(EmailToolsServiceCollectionExtensions).Assembly.GetTypes()
            .Where(type => type.Namespace == "Orkeon.Tools.Email.Dtos" && !type.IsNested)
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

        Assert.True(checkedTypes >= 8, $"Only {checkedTypes} nested types were checked.");
    }

    [Theory]
    [InlineData("email_read", "id")]
    [InlineData("email_parser", "path")]
    [InlineData("email_save_attachment", "id,directory")]
    [InlineData("email_create_folder", "path")]
    [InlineData("email_rename_folder", "path,new_name")]
    [InlineData("email_move", "ids,destination")]
    [InlineData("email_mark", "ids")]
    [InlineData("email_delete", "ids")]
    [InlineData("email_search", "")]
    [InlineData("email_send", "")]
    [InlineData("email_draft", "")]
    [InlineData("email_folders", "")]
    [InlineData("email_accounts", "")]
    public void Should_require_only_what_the_tool_cannot_do_without(string name, string required)
    {
        using var fixture = new ToolFixture();

        var parameters = fixture.Tools[name].Schema.Parameters;

        Assert.Equal(
            required.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal),
            parameters.Where(p => p.Value.Required).Select(p => p.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Should_describe_the_optional_value_parameters_as_optional()
    {
        using var fixture = new ToolFixture();

        Assert.False(fixture.Tools["email_search"].Schema.Parameters["limit"].Required);
        Assert.Equal("integer", fixture.Tools["email_search"].Schema.Parameters["limit"].Type);
        Assert.Equal("boolean", fixture.Tools["email_read"].Schema.Parameters["mark_read"].Type);
        Assert.Equal("array", fixture.Tools["email_send"].Schema.Parameters["to"].Type);
        Assert.False(fixture.Tools["email_delete"].Schema.Parameters["permanent"].Required);
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
