using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Drive.DependencyInjection;
using Orkeon.Tools.Drive.Tests.Fixtures;

namespace Orkeon.Tools.Drive.Tests.Tools;

/// <summary>
/// What every drive tool declares to the agent loop: its name, access class and category, which
/// parameters are required, and a return schema that keeps every field of its response (the
/// output filter drops any field the schema does not declare).
/// </summary>
public sealed class DriveToolContractTests
{
    [Theory]
    [InlineData("drive_accounts", "Read")]
    [InlineData("drive_list", "Read")]
    [InlineData("drive_search", "Read")]
    [InlineData("drive_info", "Read")]
    [InlineData("drive_read", "Read")]
    [InlineData("drive_download", "Edit")]
    [InlineData("drive_upload", "Edit")]
    [InlineData("drive_create_folder", "Edit")]
    [InlineData("drive_move", "Edit")]
    [InlineData("drive_delete", "Execute")]
    [InlineData("drive_share", "Execute")]
    public void Should_declare_its_name_access_and_category(string name, string access)
    {
        using var fixture = new DriveToolFixture();

        var tool = fixture.Tools[name];

        Assert.Equal(name, tool.Name);
        Assert.Equal(Enum.Parse<ToolAccess>(access), tool.Access);
        Assert.Equal("Drive", tool.Category);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Equal(name, tool.Schema.Name);
    }

    [Fact]
    public void Should_declare_every_response_field_in_the_return_schema()
    {
        using var fixture = new DriveToolFixture();

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
        using var fixture = new DriveToolFixture();
        var dtos = typeof(DriveToolsServiceCollectionExtensions).Assembly.GetTypes()
            .Where(type => type.Namespace == "Orkeon.Tools.Drive.Dtos" && !type.IsNested)
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
    [InlineData("drive_accounts", "")]
    [InlineData("drive_list", "")]
    [InlineData("drive_search", "")]
    [InlineData("drive_info", "id")]
    [InlineData("drive_read", "id")]
    [InlineData("drive_download", "id,directory")]
    [InlineData("drive_upload", "path")]
    [InlineData("drive_create_folder", "name")]
    [InlineData("drive_move", "id")]
    [InlineData("drive_delete", "id")]
    [InlineData("drive_share", "id")]
    public void Should_require_only_what_the_tool_cannot_do_without(string name, string required)
    {
        using var fixture = new DriveToolFixture();

        var parameters = fixture.Tools[name].Schema.Parameters;

        Assert.Equal(
            required.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal),
            parameters.Where(p => p.Value.Required).Select(p => p.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Should_describe_the_optional_value_parameters_with_their_types()
    {
        using var fixture = new DriveToolFixture();

        Assert.Equal("integer", fixture.Tools["drive_list"].Schema.Parameters["limit"].Type);
        Assert.Equal("boolean", fixture.Tools["drive_delete"].Schema.Parameters["permanent"].Type);
        Assert.Equal("array", fixture.Tools["drive_share"].Schema.Parameters["recipients"].Type);
        Assert.False(fixture.Tools["drive_read"].Schema.Parameters["offset"].Required);
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
