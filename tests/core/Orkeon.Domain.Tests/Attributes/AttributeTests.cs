using Orkeon.Domain.Attributes;

namespace Orkeon.Domain.Tests.Attributes;

/// <summary>
/// Tests for Domain YAML schema attributes: TypeDefinition, TypeProperty,
/// ComponentContract, ToolContract, FieldSchema, and ReturnSchema.
/// </summary>
public class AttributeTests
{
    private static readonly string[] EnumValues = ["a", "b", "c"];

    // ── TypeDefinitionAttribute ──────────────────────────────────────

    [Fact]
    public void TypeDefinition_ShouldSetName_WhenConstructedWithName()
    {
        // Arrange & Act
        var attr = new TypeDefinitionAttribute("MyType");

        // Assert
        Assert.Equal("MyType", attr.Name);
    }

    [Fact]
    public void TypeDefinition_ShouldHaveNullDescription_WhenNotSet()
    {
        // Arrange & Act
        var attr = new TypeDefinitionAttribute("MyType");

        // Assert
        Assert.Null(attr.Description);
    }

    [Fact]
    public void TypeDefinition_ShouldSetDescription_WhenAssigned()
    {
        // Arrange & Act
        var attr = new TypeDefinitionAttribute("MyType") { Description = "A custom type" };

        // Assert
        Assert.Equal("A custom type", attr.Description);
    }

    [Fact]
    public void TypeDefinition_ShouldThrowArgumentNullException_WhenNameIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TypeDefinitionAttribute(null!));
    }

    [Fact]
    public void TypeDefinition_ShouldTargetClassAndStruct()
    {
        // Arrange
        var usage = (AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(TypeDefinitionAttribute), typeof(AttributeUsageAttribute))!;

        // Assert
        Assert.NotNull(usage);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Class));
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Struct));
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    // ── TypePropertyAttribute ────────────────────────────────────────

    [Fact]
    public void TypeProperty_ShouldSetType_WhenConstructedWithType()
    {
        // Arrange & Act
        var attr = new TypePropertyAttribute("string");

        // Assert
        Assert.Equal("string", attr.Type);
    }

    [Fact]
    public void TypeProperty_ShouldHaveNullDefaults_WhenNotSet()
    {
        // Arrange & Act
        var attr = new TypePropertyAttribute("integer");

        // Assert
        Assert.Null(attr.Description);
        Assert.Null(attr.TypeDefinitionRef);
    }

    [Fact]
    public void TypeProperty_ShouldSetOptionalProperties_WhenAssigned()
    {
        // Arrange & Act
        var attr = new TypePropertyAttribute("object")
        {
            Description = "A nested object",
            TypeDefinitionRef = "Address"
        };

        // Assert
        Assert.Equal("object", attr.Type);
        Assert.Equal("A nested object", attr.Description);
        Assert.Equal("Address", attr.TypeDefinitionRef);
    }

    [Fact]
    public void TypeProperty_ShouldThrowArgumentNullException_WhenTypeIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TypePropertyAttribute(null!));
    }

    [Fact]
    public void TypeProperty_ShouldTargetProperty()
    {
        // Arrange
        var usage = (AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(TypePropertyAttribute), typeof(AttributeUsageAttribute))!;

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    // ── ComponentContractAttribute ───────────────────────────────────

    [Fact]
    public void ComponentContract_ShouldSetUniqueName_WhenConstructed()
    {
        // Arrange & Act
        var attr = new ComponentContractAttribute("my_component");

        // Assert
        Assert.Equal("my_component", attr.UniqueName);
    }

    [Fact]
    public void ComponentContract_ShouldHaveNullDefaults_WhenOptionalPropertiesNotSet()
    {
        // Arrange & Act
        var attr = new ComponentContractAttribute("comp");

        // Assert
        Assert.Null(attr.Name);
        Assert.Null(attr.Description);
        Assert.Null(attr.Version);
        Assert.Null(attr.InputSchemaRef);
        Assert.Null(attr.OutputSchemaRef);
    }

    [Fact]
    public void ComponentContract_ShouldSetAllOptionalProperties_WhenAssigned()
    {
        // Arrange & Act
        var attr = new ComponentContractAttribute("comp")
        {
            Name = "My Component",
            Description = "Does something",
            Version = "1.0.0",
            InputSchemaRef = "InputType",
            OutputSchemaRef = "OutputType"
        };

        // Assert
        Assert.Equal("My Component", attr.Name);
        Assert.Equal("Does something", attr.Description);
        Assert.Equal("1.0.0", attr.Version);
        Assert.Equal("InputType", attr.InputSchemaRef);
        Assert.Equal("OutputType", attr.OutputSchemaRef);
    }

    [Fact]
    public void ComponentContract_ShouldThrowArgumentNullException_WhenUniqueNameIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ComponentContractAttribute(null!));
    }

    [Fact]
    public void ComponentContract_ShouldTargetClass()
    {
        // Arrange
        var usage = (AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(ComponentContractAttribute), typeof(AttributeUsageAttribute))!;

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    // ── ToolContractAttribute ────────────────────────────────────────

    [Fact]
    public void ToolContract_ShouldInheritFromComponentContract()
    {
        // Assert
        Assert.True(typeof(ComponentContractAttribute).IsAssignableFrom(typeof(ToolContractAttribute)));
    }

    [Fact]
    public void ToolContract_ShouldSetUniqueName_WhenConstructed()
    {
        // Arrange & Act
        var attr = new ToolContractAttribute("my_tool");

        // Assert
        Assert.Equal("my_tool", attr.UniqueName);
    }

    [Fact]
    public void ToolContract_ShouldHaveNullCategory_WhenNotSet()
    {
        // Arrange & Act
        var attr = new ToolContractAttribute("tool");

        // Assert
        Assert.Null(attr.Category);
    }

    [Fact]
    public void ToolContract_ShouldSetCategory_WhenAssigned()
    {
        // Arrange & Act
        var attr = new ToolContractAttribute("file_read") { Category = "filesystem" };

        // Assert
        Assert.Equal("filesystem", attr.Category);
    }

    [Fact]
    public void ToolContract_ShouldInheritComponentContractProperties()
    {
        // Arrange & Act
        var attr = new ToolContractAttribute("tool")
        {
            Name = "My Tool",
            Description = "A tool",
            Version = "2.0",
            InputSchemaRef = "ToolInput",
            OutputSchemaRef = "ToolOutput",
            Category = "web"
        };

        // Assert
        Assert.Equal("tool", attr.UniqueName);
        Assert.Equal("My Tool", attr.Name);
        Assert.Equal("A tool", attr.Description);
        Assert.Equal("2.0", attr.Version);
        Assert.Equal("ToolInput", attr.InputSchemaRef);
        Assert.Equal("ToolOutput", attr.OutputSchemaRef);
        Assert.Equal("web", attr.Category);
    }

    [Fact]
    public void ToolContract_ShouldTargetClass()
    {
        // Arrange
        var usage = (AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(ToolContractAttribute), typeof(AttributeUsageAttribute))!;

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    // ── FieldSchemaAttribute ─────────────────────────────────────────

    [Fact]
    public void FieldSchema_ShouldHaveNullDefaults_WhenDefaultConstructed()
    {
        // Arrange & Act
        var attr = new FieldSchemaAttribute();

        // Assert
        Assert.Null(attr.Type);
        Assert.Null(attr.Format);
        Assert.Null(attr.Description);
        Assert.Null(attr.Required);
        Assert.Null(attr.Default);
        Assert.Null(attr.Enum);
        Assert.Null(attr.Example);
        Assert.Null(attr.TypeDefinitionRef);
        Assert.Null(attr.ItemsType);
        Assert.Null(attr.ItemsFormat);
    }

    [Fact]
    public void FieldSchema_ShouldSetAllProperties_WhenAssigned()
    {
        // Arrange & Act
        var attr = new FieldSchemaAttribute
        {
            Type = "array",
            Format = "csv",
            Description = "List of items",
            IsRequired = true,
            Default = "[]",
            Enum = EnumValues,
            Example = "[1,2,3]",
            TypeDefinitionRef = "ItemDef",
            ItemsType = "integer",
            ItemsFormat = "int32"
        };

        // Assert
        Assert.Equal("array", attr.Type);
        Assert.Equal("csv", attr.Format);
        Assert.Equal("List of items", attr.Description);
        Assert.True(attr.Required);
        Assert.True(attr.IsRequired);
        Assert.Equal("[]", attr.Default);
        Assert.Equal(EnumValues, attr.Enum);
        Assert.Equal("[1,2,3]", attr.Example);
        Assert.Equal("ItemDef", attr.TypeDefinitionRef);
        Assert.Equal("integer", attr.ItemsType);
        Assert.Equal("int32", attr.ItemsFormat);
    }

    [Fact]
    public void FieldSchema_IsRequiredShouldDefaultToTrue_WhenRequiredIsNull()
    {
        // Arrange
        var attr = new FieldSchemaAttribute();

        // Act & Assert
        Assert.Null(attr.Required);
        Assert.True(attr.IsRequired); // Default when Required is null
    }

    [Fact]
    public void FieldSchema_IsRequiredShouldSetRequired_WhenAssigned()
    {
        // Arrange
        var attr = new FieldSchemaAttribute();

        // Act
        attr.IsRequired = false;

        // Assert
        Assert.False(attr.Required);
        Assert.False(attr.IsRequired);
    }

    [Fact]
    public void FieldSchema_ShouldTargetProperty()
    {
        // Arrange
        var usage = (AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(FieldSchemaAttribute), typeof(AttributeUsageAttribute))!;

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }

    // ── ReturnSchemaAttribute ────────────────────────────────────────

    [Fact]
    public void ReturnSchema_ShouldHaveNullDefaults_WhenDefaultConstructed()
    {
        // Arrange & Act
        var attr = new ReturnSchemaAttribute();

        // Assert
        Assert.Null(attr.Type);
        Assert.Null(attr.Format);
        Assert.Null(attr.Description);
        Assert.Null(attr.Example);
        Assert.Null(attr.TypeDefinitionRef);
        Assert.Null(attr.ItemsType);
        Assert.Null(attr.ItemsFormat);
    }

    [Fact]
    public void ReturnSchema_ShouldSetAllProperties_WhenAssigned()
    {
        // Arrange & Act
        var attr = new ReturnSchemaAttribute
        {
            Type = "string",
            Format = "date-time",
            Description = "The creation date",
            Example = "2026-01-01T00:00:00Z",
            TypeDefinitionRef = "DateTimeDef",
            ItemsType = "string",
            ItemsFormat = "uuid"
        };

        // Assert
        Assert.Equal("string", attr.Type);
        Assert.Equal("date-time", attr.Format);
        Assert.Equal("The creation date", attr.Description);
        Assert.Equal("2026-01-01T00:00:00Z", attr.Example);
        Assert.Equal("DateTimeDef", attr.TypeDefinitionRef);
        Assert.Equal("string", attr.ItemsType);
        Assert.Equal("uuid", attr.ItemsFormat);
    }

    [Fact]
    public void ReturnSchema_ShouldTargetProperty()
    {
        // Arrange
        var usage = (AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
            typeof(ReturnSchemaAttribute), typeof(AttributeUsageAttribute))!;

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Property, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(usage.Inherited);
    }
}
