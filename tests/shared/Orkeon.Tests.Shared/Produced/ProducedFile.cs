using System.Xml.Linq;
using Xunit;

namespace Orkeon.Tests.Shared.Produced;

/// <summary>
/// A file of the repository that code produces and a test holds: the test computes what the file
/// should be and fails when the committed one differs, naming the command that writes it again.
/// Run with <see cref="WriteVariable"/> set to <c>1</c>, the same test writes the file instead of
/// failing — the "regenerate" and "check" of one producer.
/// </summary>
public static class ProducedFile
{
    /// <summary>The environment variable that turns the check into a write.</summary>
    public const string WriteVariable = "UPDATE_PRODUCED_FILES";

    /// <summary>Whether this run writes the produced files.</summary>
    public static bool Writing =>
        Environment.GetEnvironmentVariable(WriteVariable) is "1" or "true";

    /// <summary>The repository's root: the first directory above the running tests that holds <c>Orkeon.sln</c>.</summary>
    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No Orkeon.sln above {AppContext.BaseDirectory}: the tests do not run from the repository.");
    }

    /// <summary>The text of the file at <paramref name="repositoryPath"/>, line feeds only, or null when it is not there.</summary>
    /// <param name="repositoryPath">The file's path from the repository's root, with forward slashes.</param>
    public static string? Read(string repositoryPath)
    {
        var path = Physical(repositoryPath);
        return File.Exists(path) ? Normalize(File.ReadAllText(path)) : null;
    }

    /// <summary>
    /// Holds the file at <paramref name="repositoryPath"/> to <paramref name="produced"/>: equal once
    /// line endings are set aside, or the test fails with <paramref name="regenerate"/>. With
    /// <see cref="WriteVariable"/> set, writes it — line feeds, no byte order mark — and passes.
    /// </summary>
    /// <param name="repositoryPath">The file's path from the repository's root, with forward slashes.</param>
    /// <param name="produced">What the file should hold.</param>
    /// <param name="regenerate">The command that writes the file again, for the failure message.</param>
    public static void AssertCurrent(string repositoryPath, string produced, string regenerate)
    {
        ArgumentNullException.ThrowIfNull(produced);

        var expected = Normalize(produced);
        if (Writing)
        {
            var path = Physical(repositoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
            return;
        }

        var committed = Read(repositoryPath);
        if (string.Equals(committed, expected, StringComparison.Ordinal))
            return;

        Assert.Fail(
            $"{repositoryPath} is not what the code produces{FirstDifference(committed, expected)}. " +
            $"Write it again: {regenerate}");
    }

    /// <summary>
    /// The XML documentation files the build wrote beside the running tests — one per Orkeon assembly
    /// they reference —, which is where a produced file takes its sentences from.
    /// </summary>
    public static IReadOnlyList<XDocument> Documentation() =>
    [
        .. Directory.EnumerateFiles(AppContext.BaseDirectory, "*.xml")
            .Where(file => Path.GetFileName(file).StartsWith("orkeon", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(file => XDocument.Load(file)),
    ];

    private static string Physical(string repositoryPath) =>
        Path.Combine(RepositoryRoot(), repositoryPath.Replace('/', Path.DirectorySeparatorChar));

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FirstDifference(string? committed, string expected)
    {
        if (committed is null)
            return " (the file is missing)";

        var committedLines = committed.Split('\n');
        var expectedLines = expected.Split('\n');
        for (var index = 0; index < Math.Max(committedLines.Length, expectedLines.Length); index++)
        {
            var was = index < committedLines.Length ? committedLines[index] : "<end of file>";
            var now = index < expectedLines.Length ? expectedLines[index] : "<end of file>";
            if (!string.Equals(was, now, StringComparison.Ordinal))
                return $" (line {index + 1}: committed `{was.Trim()}`, produced `{now.Trim()}`)";
        }

        return string.Empty;
    }
}
