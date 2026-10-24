using System.Reflection;
using Orkeon.Constants.Protocol;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// The event-kind catalogues of <c>Orkeon.Constants.Protocol</c> are what a consumer of the CLI's
/// JSON streams asserts it handles: an <c>All</c> that forgets a declared kind turns that check into
/// a silent gap, the drift the catalogues exist to prevent.
/// </summary>
public class ProtocolEventKindsTests
{
    public static TheoryData<Type> Catalogues =>
    [
        typeof(RunEventKinds),
        typeof(DriveEventKinds),
        typeof(EmailEventKinds),
        typeof(UseCaseEventKinds),
    ];

    [Theory]
    [MemberData(nameof(Catalogues))]
    public void ShouldListEveryDeclaredKindOnce_WhenAllIsRead(Type catalogue)
    {
        var declared = DeclaredKinds(catalogue);
        var all = ListOf(catalogue, "All");

        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(declared.Order(StringComparer.Ordinal), all.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(typeof(DriveEventKinds))]
    [InlineData(typeof(EmailEventKinds))]
    public void ShouldKeepEachVerbListInsideAll_WhenTheCatalogueSplitsByVerb(Type catalogue)
    {
        var all = ListOf(catalogue, "All");

        foreach (var verb in new[] { "Login", "Check" })
        {
            var kinds = ListOf(catalogue, verb);
            Assert.NotEmpty(kinds);
            Assert.All(kinds, kind => Assert.Contains(kind, all));
            Assert.Contains(GetConstant(catalogue, "Error"), kinds);
        }
    }

    [Fact]
    public void ShouldSpellEveryKindInLowercaseDottedForm_WhenItGoesOnTheWire()
    {
        foreach (var catalogue in Catalogues.Select(row => row.Data))
        {
            Assert.All(DeclaredKinds(catalogue), kind =>
                Assert.Matches("^[a-z]+(\\.[a-z_]+)*$", kind));
        }
    }

    private static List<string> DeclaredKinds(Type catalogue) =>
        [.. catalogue.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)];

    private static IReadOnlyList<string> ListOf(Type catalogue, string property) =>
        (IReadOnlyList<string>)catalogue.GetProperty(property, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static string GetConstant(Type catalogue, string name) =>
        (string)catalogue.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
}
