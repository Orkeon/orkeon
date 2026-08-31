using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Two ways a button can be alive, ungreyed, and do nothing at all — both found in this app,
/// both invisible to a compiler and to every behavioural test, because nothing is broken in
/// any one file. They only show up when the pieces are read together.
/// <para>
/// (1) A ViewModel raises an event that nobody subscribes to. That is how «Edit» on the chat's
/// brief card came to be a second «close»: the intent went out and landed nowhere.
/// (2) A <c>Command=</c> binding names a member no ViewModel has. WPF leaves Command null,
/// the button stays enabled, and every click is swallowed in silence.
/// </para>
/// <para>
/// Read as text from the source tree, so the rules hold on the Linux runner with no WPF.
/// </para>
/// </summary>
public sealed partial class GestureReachesSomethingTests
{
    /// <summary>
    /// ICommand's own event. WPF's CommandManager subscribes to it inside PresentationFramework,
    /// so no <c>+=</c> for it appears in this repository and it can never be "orphaned" here.
    /// </summary>
    private static readonly string[] SubscribedByWpfItself = ["CanExecuteChanged"];

    [GeneratedRegex(@"public\s+event\s+[\w<>?,\s\.]+?\s+(\w+)\s*;")]
    private static partial Regex EventDeclarationPattern();

    [GeneratedRegex(@"(\w+)\?\.Invoke\(")]
    private static partial Regex EventRaisePattern();

    [GeneratedRegex(@"(?:\.|\b)(\w+)\s*\+=")]
    private static partial Regex EventSubscriptionPattern();

    [GeneratedRegex("""public\s+(?:Async)?RelayCommand\s+(\w+)\s*\{""")]
    private static partial Regex CommandDeclarationPattern();

    // Escaped, not raw: the pattern ends with a quote, which would run into the delimiter.
    [GeneratedRegex("Command=\"\\{Binding\\s+([^}\"]*)\\}\"")]
    private static partial Regex CommandBindingPattern();

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static IEnumerable<string> SourceFiles(string extension) =>
        Directory.EnumerateFiles(WpfSourceRoot(), extension, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Every_event_a_gesture_raises_has_somebody_listening()
    {
        var code = SourceFiles("*.cs").Select(File.ReadAllText).ToList();
        var joined = string.Join("\n", code);

        var declared = code.SelectMany(t => EventDeclarationPattern().Matches(t).Cast<Match>())
            .Select(m => m.Groups[1].Value)
            .Where(n => !SubscribedByWpfItself.Contains(n, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var raised = EventRaisePattern().Matches(joined).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var listened = EventSubscriptionPattern().Matches(joined).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var orphans = declared.Where(raised.Contains).Where(n => !listened.Contains(n)).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            orphans.Count == 0,
            "These events are raised and nobody subscribes, so the gesture behind each one ends "
            + "in silence — the button reacts and the app does nothing about it. Wire a listener, "
            + "or delete the event and the command that raises it: "
            + string.Join(", ", orphans));
    }

    [Fact]
    public void Every_command_binding_names_a_command_that_exists()
    {
        var commands = SourceFiles("*.cs")
            .SelectMany(f => CommandDeclarationPattern().Matches(File.ReadAllText(f)).Cast<Match>())
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
        {
            var markup = File.ReadAllText(file);
            foreach (var match in CommandBindingPattern().Matches(markup).Cast<Match>())
            {
                var path = match.Groups[1].Value.Split(',')[0].Trim();
                if (path.StartsWith("Path=", StringComparison.Ordinal))
                    path = path[5..].Trim();

                // A binding steered by RelativeSource/ElementName resolves against another
                // element entirely; the leaf is still the command name we care about.
                if (path.Length == 0 || path.StartsWith("RelativeSource", StringComparison.Ordinal)
                    || path.StartsWith("ElementName", StringComparison.Ordinal))
                    continue;

                var leaf = path.Split('.')[^1];
                if (commands.Contains(leaf))
                    continue;

                var line = markup.Take(match.Index).Count(c => c == '\n') + 1;
                unknown.Add($"{Path.GetFileName(file)}:{line} binds {leaf}");
            }
        }

        Assert.True(
            unknown.Count == 0,
            "No ViewModel declares these commands. A Command binding that cannot resolve leaves "
            + "Command null: WPF keeps the button enabled and drops every click without a word. "
            + string.Join("; ", unknown));
    }
}
