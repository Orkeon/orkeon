namespace Orkeon.Compliance.Vfs.Tests;

public class ConsoleUsageAnalyzerTests
{
    private const string SuppressAttributeSource = """

        namespace Orkeon.Compliance.Vfs
        {
            [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false, Inherited = false)]
            public sealed class SuppressConsoleComplianceAttribute : System.Attribute
            {
                public SuppressConsoleComplianceAttribute(string reason) { Reason = reason; }
                public string Reason { get; }
            }
        }
        """;

    private const string FrameworkPath = "/workspace/src/core/Orkeon.Application/Foo.cs";

    [Fact]
    public async System.Threading.Tasks.Task Console_WriteLine_Reports_ORKCON001()
    {
        const string source = """
            public class C
            {
                public void Say() => System.Console.WriteLine("hi");
            }
            """;

        var diagnostics = await AnalyzerHarness<ConsoleUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKCON001", diagnostics[0].Id);
        Assert.Contains("WriteLine", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async System.Threading.Tasks.Task Console_Streams_Properties_Events_And_Method_Groups_Report()
    {
        const string source = """
            using System;
            using System.IO;
            public class C
            {
                public TextWriter Out() => Console.Error;
                public bool Redirected() => Console.IsOutputRedirected;
                public void Colour() => Console.ForegroundColor = ConsoleColor.Red;
                public void Cancel() => Console.CancelKeyPress += (_, _) => { };
                public Action<string> Writer() => Console.WriteLine;
            }
            """;

        var diagnostics = await AnalyzerHarness<ConsoleUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Equal(
            ["Error", "IsOutputRedirected", "ForegroundColor", "CancelKeyPress", "WriteLine"],
            diagnostics.Select(d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Split('\'')[1].Replace("System.Console.", "", StringComparison.Ordinal)));
    }

    [Fact]
    public async System.Threading.Tasks.Task Console_ViaUsingStatic_Reports_ORKCON001()
    {
        const string source = """
            using static System.Console;
            public class C
            {
                public void Say() => WriteLine("hi");
            }
            """;

        var diagnostics = await AnalyzerHarness<ConsoleUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKCON001", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task Property_Named_Console_Of_Another_Type_Is_Not_Reported()
    {
        const string source = """
            public interface IConsoleAdapter { void WriteLine(string text); }
            public class C
            {
                private IConsoleAdapter Console { get; } = null!;
                public void Say() => Console.WriteLine("hi");
            }
            """;

        var diagnostics = await AnalyzerHarness<ConsoleUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task Console_Text_Inside_A_String_Is_Not_Reported()
    {
        const string source = """
            public class C
            {
                public string ChildProgram() => "System.Console.WriteLine(42);";
            }
            """;

        var diagnostics = await AnalyzerHarness<ConsoleUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task No_Diagnostic_When_Suppressed_On_Class()
    {
        const string source = """
            using Orkeon.Compliance.Vfs;
            [SuppressConsoleCompliance("CONSOLE-ADAPTER: the real console")]
            public class SystemConsoleAdapter
            {
                public void Say() => System.Console.WriteLine("hi");
            }
            """ + SuppressAttributeSource;

        var diagnostics = await AnalyzerHarness<ConsoleUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task No_Diagnostic_Under_Tests()
    {
        const string source = """
            public class C
            {
                public void Say() => System.Console.WriteLine("hi");
            }
            """;

        var diagnostics = await AnalyzerHarness<ConsoleUsageAnalyzer>.RunAsync(source, "/workspace/tests/core/Foo.Tests/C.cs");

        Assert.Empty(diagnostics);
    }
}
