using Orkeon.Constants.Configuration;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #469 - the binder's refusal reworded without its value (<see cref="BinderRefusal"/>), shared by the
/// host's settings check and the e-mail and drive binders: whatever the key path or the value holds,
/// nothing of the value survives.
/// </summary>
public sealed class BinderRefusalTests
{
    private const string Withheld =
        "Failed to convert a configuration value; the binder's message is withheld, since it quotes the value.";

    // Key-shaped, built at run time.
    private static readonly string Value = string.Concat("sk-", "ant-", Guid.NewGuid().ToString("N"));

    private static string Sentence(string value, string path, string type) =>
        $"Failed to convert configuration value '{value}' at '{path}' to type '{type}'.";

    [Theory]
    [Trait("BR", "CH-469")]
    [InlineData("Orkeon:Decision:Profiles:l'equipe:FailClosed")]
    [InlineData("Orkeon:Decision:Profiles:l'equipe d'Anne:Stages:0:BlockThreshold")]
    [InlineData("Orkeon:Tools:Email:Accounts:'a'':Incoming:Port")]
    public void ShouldKeepThePathAndDropTheValue_WhenThePathHoldsApostrophes(string path)
    {
        var refusal = BinderRefusal.WithoutValue(Sentence("sk-secret-example", path, "System.Boolean"));

        Assert.Equal($"Failed to convert the configuration value at '{path}' to type 'System.Boolean'.", refusal);
    }

    [Theory]
    [Trait("BR", "CH-469")]
    [InlineData("sk-secret' at 'Fake:Key' to type 'System.String")]
    [InlineData("sk-secret' at 'Fake:Key' to type 'System.String'.")]
    [InlineData("sk-secret'")]
    [InlineData("'sk-secret' at '")]
    [InlineData("")]
    public void ShouldDropTheValue_WhateverItHolds(string value)
    {
        var refusal = BinderRefusal.WithoutValue(Sentence(value, "Orkeon:Decision:Enabled", "System.Boolean"));

        Assert.Equal("Failed to convert the configuration value at 'Orkeon:Decision:Enabled' to type 'System.Boolean'.", refusal);
    }

    [Fact]
    [Trait("BR", "CH-469")]
    public void ShouldShowThePathTailOnly_WhenThePathHoldsTheSeparator()
    {
        var refusal = BinderRefusal.WithoutValue(Sentence("sk-secret-example", "Orkeon:Decision:Profiles:x' at 'y:FailClosed", "System.Boolean"));

        Assert.Equal("Failed to convert the configuration value at 'y:FailClosed' to type 'System.Boolean'.", refusal);
    }

    [Theory]
    [Trait("BR", "CH-469")]
    [InlineData("Cannot create instance of type 'Orkeon.X' because it is either abstract or an interface.")]
    [InlineData("another message")]
    [InlineData("")]
    public void ShouldKeepAMessage_WhenItIsNotTheBindersSentence(string message)
    {
        Assert.Equal(message, BinderRefusal.WithoutValue(message));
    }

    [Theory]
    [Trait("BR", "CH-469")]
    [InlineData("Failed to convert configuration value 'sk-secret-example' at 'A:B' to type 'Sys'tem'.")]
    [InlineData("Failed to convert configuration value 'sk-secret-example' at 'A:B' to type ''.")]
    [InlineData("Failed to convert configuration value 'sk-secret-example' at '' to type 'System.Boolean'.")]
    [InlineData("Failed to convert configuration value 'sk-secret-example' at 'A:B'")]
    [InlineData("Failed to convert configuration value 'sk-secret-example' to type 'System.Boolean'.")]
    [InlineData("Options failed: Failed to convert configuration value 'sk-secret-example' at 'A:B' to type 'System.Boolean'.")]
    [InlineData("Error_FailedBinding, sk-secret-example, A:B, System.Boolean")]
    public void ShouldWithholdTheMessage_WhenItQuotesAValueItCannotReadBack(string message)
    {
        var refusal = BinderRefusal.WithoutValue(message);

        Assert.Equal(Withheld, refusal);
        Assert.Equal(
            "Failed to convert a configuration value under 'Orkeon:Tools:Email:Accounts:l'equipe'; the binder's message is withheld, since it quotes the value.",
            BinderRefusal.WithoutValue(message, "Orkeon:Tools:Email:Accounts:l'equipe"));
    }

    [Fact]
    [Trait("BR", "CH-469")]
    public void ShouldTellTheChainQuotesAValue_WhenAnInnerExceptionIsTheBindersSentence()
    {
        var binder = new InvalidOperationException(Sentence("sk-secret-example", "A:B", "System.Boolean"), new FormatException("sk-secret-example is not a valid value for Boolean."));

        Assert.True(BinderRefusal.QuotesAValue(binder));
        Assert.True(BinderRefusal.QuotesAValue(new InvalidOperationException("wrapped", binder)));
        Assert.False(BinderRefusal.QuotesAValue(new InvalidOperationException("a registration's own refusal")));
        Assert.False(BinderRefusal.QuotesAValue(null));
    }

    /// <summary>The converters' and parsers' messages, each quoting the value it refused.</summary>
    public static TheoryData<string> ConverterMessages() =>
    [
        $"{Value} is not a valid value for TimeSpan.",
        $"Requested value '{Value}' was not found.",
        $"The input string '{Value}' was not in a correct format.",
        $"String '{Value}' was not recognized as a valid Boolean.",
    ];

    [Theory]
    [Trait("BR", "CH-469")]
    [MemberData(nameof(ConverterMessages))]
    public void ShouldTellTheChainQuotesAValue_WhenItHoldsAConverterMessageAlone(string message)
    {
        Assert.True(BinderRefusal.QuotesAValue(new InvalidOperationException("wrapped", new ArgumentException(message))));
        Assert.Equal(Withheld, BinderRefusal.WithoutValue(message));
    }

    [Theory]
    [Trait("BR", "CH-469")]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(UriFormatException))]
    [InlineData(typeof(OverflowException))]
    [InlineData(typeof(InvalidCastException))]
    public void ShouldTellTheChainQuotesAValue_WhenItHoldsAConversionException(Type type)
    {
        var conversion = (Exception)Activator.CreateInstance(type, "a message of any shape")!;

        Assert.True(BinderRefusal.QuotesAValue(new InvalidOperationException("wrapped", conversion)));
    }

    [Fact]
    [Trait("BR", "CH-469")]
    public void ShouldReadTheBindersSentenceFromTheChain_AndNeverPrintAMessageThatQuotesTheValue()
    {
        var converter = new FormatException($"{Value} is not a valid value for TimeSpan.");
        var binder = new InvalidOperationException(Sentence(Value, "MCP:Servers:x:CallTimeout", "System.TimeSpan"), converter);

        Assert.Equal(
            "Failed to convert the configuration value at 'MCP:Servers:x:CallTimeout' to type 'System.TimeSpan'.",
            BinderRefusal.WithoutValue(new InvalidOperationException("wrapped", binder), "MCP:Servers:x"));
        Assert.Equal(
            "Failed to convert a configuration value under 'MCP:Servers:x'; the binder's message is withheld, since it quotes the value.",
            BinderRefusal.WithoutValue(new InvalidOperationException("wrapped", converter), "MCP:Servers:x"));
        Assert.Equal("a registration's own refusal", BinderRefusal.WithoutValue(new InvalidOperationException("a registration's own refusal")));
    }
}
