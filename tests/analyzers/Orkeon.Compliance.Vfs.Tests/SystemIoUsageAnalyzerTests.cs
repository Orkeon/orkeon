namespace Orkeon.Compliance.Vfs.Tests;

public class SystemIoUsageAnalyzerTests
{
    private const string SuppressAttributeSource = """

        namespace Orkeon.Compliance.Vfs
        {
            [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false, Inherited = false)]
            public sealed class SuppressVfsComplianceAttribute : System.Attribute
            {
                public SuppressVfsComplianceAttribute(string reason) { Reason = reason; }
                public string Reason { get; }
            }
        }
        """;

    private const string FrameworkPath = "/workspace/src/core/Orkeon.Application/Foo.cs";

    [Fact]
    public async System.Threading.Tasks.Task File_ReadAllText_Reports_ORKVFS001()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public string Read() => File.ReadAllText("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS001", diagnostics[0].Id);
        Assert.Contains("ReadAllText", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    // Two shapes the analyzer could not see. Neither exists in src today, which is exactly
    // why they are worth pinning: the gate is what keeps them from appearing.

    [Fact]
    public async System.Threading.Tasks.Task File_ReadAllText_ViaUsingStatic_Reports_ORKVFS001()
    {
        const string source = """
            using static System.IO.File;
            public class C
            {
                public string Read() => ReadAllText("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS001", diagnostics[0].Id);
        Assert.Contains("ReadAllText", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async System.Threading.Tasks.Task FileStream_TargetTyped_New_Reports_ORKVFS003()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public void Open()
                {
                    FileStream fs = new("/tmp/foo", FileMode.Open);
                    fs.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS003", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamReader_TargetTyped_New_Reports_ORKVFS006()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public void Read()
                {
                    StreamReader r = new("/tmp/foo");
                    r.Dispose();
                }
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS006", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task Directory_Exists_Reports_ORKVFS002()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public bool Has() => Directory.Exists("/tmp");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS002", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task FileStream_String_Ctor_Reports_ORKVFS003()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public Stream Open() => new FileStream("/tmp/foo", FileMode.Open);
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS003", diagnostics[0].Id);
        Assert.Contains("FileStream", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async System.Threading.Tasks.Task FileInfo_String_Ctor_Reports_ORKVFS003()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public FileInfo Info() => new FileInfo("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS003", diagnostics[0].Id);
        Assert.Contains("FileInfo", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async System.Threading.Tasks.Task Path_GetFullPath_Reports_ORKVFS004_Error()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public string Resolve(string input) => Path.GetFullPath(input);
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS004", diagnostics[0].Id);
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostics[0].Severity);
    }

    [Fact]
    public async System.Threading.Tasks.Task FileSystemWatcher_Ctor_Reports_ORKVFS005()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public FileSystemWatcher Watch() => new FileSystemWatcher("/tmp");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS005", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task No_Diagnostic_When_Suppressed_On_Method()
    {
        var source = $$"""
            using System.IO;
            using Orkeon.Compliance.Vfs;
            public class C
            {
                [SuppressVfsCompliance("bootstrap mount")]
                public string Read() => File.ReadAllText("/tmp/foo");
            }
            {{SuppressAttributeSource}}
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task No_Diagnostic_When_Suppressed_On_Class()
    {
        var source = $$"""
            using System.IO;
            using Orkeon.Compliance.Vfs;

            [SuppressVfsCompliance("VFS implementation")]
            public class C
            {
                public string Read() => File.ReadAllText("/tmp/foo");
                public bool Has() => Directory.Exists("/tmp");
            }
            {{SuppressAttributeSource}}
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task No_Diagnostic_When_FileSystem_Folder_Exempted()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public string Read() => File.ReadAllText("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(
            source,
            "/workspace/src/core/Orkeon.Domain/FileSystem/FileSystemService.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task No_Diagnostic_In_Tests_Folder()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public string Read() => File.ReadAllText("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(
            source,
            "/workspace/tests/something/Foo.cs");

        Assert.Empty(diagnostics);
    }

    // R2.7 — exemptions are no longer granted by whole-folder substring for security
    // primitives. A NEW file dropped under .../Sandbox/ (not on the explicit allowlist)
    // must now be audited; only the named legitimate files stay exempt, and any other
    // raw I/O must carry an explicit [SuppressVfsCompliance] marker.

    [Fact]
    public async System.Threading.Tasks.Task New_File_Under_Sandbox_Folder_Is_No_Longer_Exempted()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public string Read() => File.ReadAllText("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(
            source,
            "/workspace/src/core/Orkeon.Infrastructure/Sandbox/NewSandboxHelper.cs");

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS001", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task New_File_Under_Sandbox_Folder_Silenced_By_Suppress_Attribute()
    {
        var source = $$"""
            using System.IO;
            using Orkeon.Compliance.Vfs;
            public class C
            {
                [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: provisions a sandbox mount before DI")]
                public string Read() => File.ReadAllText("/tmp/foo");
            }
            {{SuppressAttributeSource}}
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(
            source,
            "/workspace/src/core/Orkeon.Infrastructure/Sandbox/NewSandboxHelper.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task Named_Sandbox_Bootstrap_File_Remains_Exempted()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public string Read() => File.ReadAllText("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(
            source,
            "/workspace/src/core/Orkeon.Infrastructure/Sandbox/SandboxSession.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task Named_PathValidator_File_Remains_Exempted()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public string Resolve(string p) => Path.GetFullPath(p);
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(
            source,
            "/workspace/src/core/Orkeon.Infrastructure/Security/PathValidator.cs");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamReader_String_Ctor_Reports_ORKVFS006()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public StreamReader Open() => new StreamReader("/tmp/foo");
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS006", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task StreamReader_On_Stream_Does_Not_Report()
    {
        const string source = """
            using System.IO;
            public class C
            {
                public StreamReader Wrap(Stream s) => new StreamReader(s);
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async System.Threading.Tasks.Task Nullable_FileSystemService_Field_Reports_ORKVFS007()
    {
        const string source = """
            public interface IFileSystemService { }
            public class C
            {
                private readonly IFileSystemService? _fs;
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Single(diagnostics);
        Assert.Equal("ORKVFS007", diagnostics[0].Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task NonNullable_FileSystemService_Field_Does_Not_Report()
    {
        const string source = """
            public interface IFileSystemService { }
            public class C
            {
                private readonly IFileSystemService _fs = null!;
            }
            """;

        var diagnostics = await AnalyzerHarness<SystemIoUsageAnalyzer>.RunAsync(source, FrameworkPath);

        Assert.Empty(diagnostics);
    }
}
