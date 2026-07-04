using System.Collections.Immutable;
using Jint;
using Jint.Native;
using Orkeon.Cli.Scripting.Loading;

namespace Orkeon.Cli.Scripting.Tests.Loading;

public sealed class CommandDescriptorValidatorTests
{
    private static readonly JsValue HandlerStub = new Engine().Evaluate("(function(){})");

    private static readonly string[] DeployAlias = ["deploy"];
    private static readonly string[] HelpAlias = ["help"];
    private static readonly string[] DuplicateAlias = ["d", "d"];
    private static readonly string[] SingleD = ["d"];

    private static CommandDescriptor MakeDescriptor(
        string name = "deploy",
        string description = "Deploy something.",
        IEnumerable<string>? aliases = null)
        => new()
        {
            SourceVirtualPath = "/cmd/deploy.cmd.ts",
            Name = name,
            Aliases = (aliases ?? Array.Empty<string>()).ToImmutableArray(),
            Description = description,
            Handler = HandlerStub,
        };

    [Fact]
    public void Validate_returns_success_for_well_formed_descriptor()
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor());
        Assert.True(result.IsValid);
        Assert.Null(result.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Deploy")]            // uppercase letter
    [InlineData("1deploy")]           // starts with digit
    [InlineData("deploy!")]           // illegal char
    [InlineData("de ploy")]           // whitespace
    public void Validate_rejects_invalid_names(string name)
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(name: name));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.InvalidName, result.Failure);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("exit")]
    [InlineData("clear")]
    public void Validate_rejects_reserved_names(string name)
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(name: name));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.NameIsBuiltin, result.Failure);
    }

    [Fact]
    public void Validate_rejects_alias_equal_to_name()
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(aliases: DeployAlias));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.AliasEqualsName, result.Failure);
    }

    [Fact]
    public void Validate_rejects_alias_colliding_with_reserved()
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(aliases: HelpAlias));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.NameIsBuiltin, result.Failure);
    }

    [Fact]
    public void Validate_rejects_duplicate_alias()
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(aliases: DuplicateAlias));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.InvalidAlias, result.Failure);
    }

    [Theory]
    [InlineData("alias with space")]
    [InlineData("UPPER")]
    [InlineData("")]
    public void Validate_rejects_invalid_aliases(string alias)
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(aliases: new[] { alias }));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.InvalidAlias, result.Failure);
    }

    [Fact]
    public void Validate_rejects_description_with_newline()
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(description: "line1\nline2"));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.InvalidDescription, result.Failure);
    }

    [Fact]
    public void Validate_rejects_description_too_long()
    {
        var huge = new string('x', CommandDescriptorValidator.MaxDescriptionLength + 1);
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(description: huge));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.InvalidDescription, result.Failure);
    }

    [Fact]
    public void Validate_rejects_empty_description()
    {
        var result = CommandDescriptorValidator.Validate(MakeDescriptor(description: ""));
        Assert.False(result.IsValid);
        Assert.Equal(ValidationFailure.InvalidDescription, result.Failure);
    }

    [Fact]
    public void ValidateGlobalUniqueness_returns_empty_when_no_conflict()
    {
        var conflicts = CommandDescriptorValidator.ValidateGlobalUniqueness(new[]
        {
            MakeDescriptor(name: "deploy"),
            MakeDescriptor(name: "status"),
        });
        Assert.Empty(conflicts);
    }

    [Fact]
    public void ValidateGlobalUniqueness_first_wins_on_name_collision()
    {
        var a = MakeDescriptor(name: "deploy") with { SourceVirtualPath = "/cmd/a.cmd.ts" };
        var b = MakeDescriptor(name: "deploy") with { SourceVirtualPath = "/cmd/b.cmd.ts" };
        var conflicts = CommandDescriptorValidator.ValidateGlobalUniqueness(new[] { a, b });
        Assert.Single(conflicts);
        Assert.Equal(b, conflicts[0].Conflicting);
        Assert.Contains("/cmd/a.cmd.ts", conflicts[0].Reason);
    }

    [Fact]
    public void ValidateGlobalUniqueness_detects_alias_collision()
    {
        var first = MakeDescriptor(name: "deploy", aliases: SingleD)
            with { SourceVirtualPath = "/cmd/a.cmd.ts" };
        var second = MakeDescriptor(name: "destroy", aliases: SingleD)
            with { SourceVirtualPath = "/cmd/b.cmd.ts" };
        var conflicts = CommandDescriptorValidator.ValidateGlobalUniqueness(new[] { first, second });
        Assert.Single(conflicts);
        Assert.Contains("Alias 'd'", conflicts[0].Reason);
    }
}
