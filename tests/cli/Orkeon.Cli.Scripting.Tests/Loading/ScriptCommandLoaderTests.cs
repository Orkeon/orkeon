using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Scripting.Loading;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Scripting.Tests.Loading;

/// <summary>
/// Integration tests for <see cref="ScriptCommandLoader"/> that exercise the discovery →
/// materialise → validate pipeline on real disk fixtures via
/// <see cref="DiskBackedFileSystemService"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="PassThroughTranspiler"/> to bypass esbuild — the fixtures are written
/// as plain JS-in-TS-syntax so we don't depend on the npm-installed toolchain when
/// running unit tests offline.
/// </remarks>
public sealed class ScriptCommandLoaderTests : IDisposable
{
    private static readonly ImmutableArray<string> CmdDirectories = ["/cmd"];

    private readonly string _tempDir;

    public ScriptCommandLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-cli-scripting-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private void CopyFixture(string relativeName, string targetName)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativeName);
        var dst = Path.Combine(_tempDir, targetName);
        File.Copy(src, dst, overwrite: true);
    }

    private ScriptCommandLoader BuildLoader(ScriptCommandLoaderOptions options, ILoggerFactory? logFactory = null)
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/cmd");
        return new ScriptCommandLoader(
            fileSystem: fs,
            transpiler: PassThroughTranspiler.Instance,
            engineFactory: new JsEngineFactory(),
            options: options,
            loggerFactory: logFactory);
    }

    [Fact]
    public async Task Returns_empty_registry_when_disabled()
    {
        CopyFixture("valid-script.cmd.ts", "valid-script.cmd.ts");
        var loader = BuildLoader(new ScriptCommandLoaderOptions { Enabled = false });
        var (registry, summary) = await loader.LoadAndRegisterAsync(CancellationToken.None);
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, summary.Loaded);
    }

    [Fact]
    public async Task Loads_single_valid_script()
    {
        CopyFixture("valid-script.cmd.ts", "valid-script.cmd.ts");
        var loader = BuildLoader(new ScriptCommandLoaderOptions
        {
            Directories = CmdDirectories,
        });

        var (registry, summary) = await loader.LoadAndRegisterAsync(CancellationToken.None);

        Assert.Equal(1, registry.Count);
        Assert.Equal(1, summary.Loaded);
        Assert.Equal(0, summary.SkippedScripts);
        Assert.Equal(0, summary.Conflicts);
        var cmd = Assert.Single(registry.ScriptCommands);
        Assert.Equal("hello", cmd.Name);
    }

    [Fact]
    public async Task Skips_script_with_syntax_error_and_continues_when_resilient()
    {
        CopyFixture("valid-script.cmd.ts", "valid-script.cmd.ts");
        CopyFixture("invalid-syntax.cmd.ts", "invalid-syntax.cmd.ts");

        using var logFactory = new TestLogFactory();
        var loader = BuildLoader(new ScriptCommandLoaderOptions
        {
            Directories = CmdDirectories,
            FailFastOnInvalidScript = false,
        }, logFactory);

        var (registry, summary) = await loader.LoadAndRegisterAsync(CancellationToken.None);

        Assert.Equal(1, registry.Count);
        Assert.Equal(1, summary.SkippedScripts);
        Assert.Contains(logFactory.Records, r => r.Level == LogLevel.Error && r.Message.Contains("invalid-syntax"));
    }

    [Fact]
    public async Task Throws_on_first_invalid_script_when_strict()
    {
        CopyFixture("invalid-syntax.cmd.ts", "invalid-syntax.cmd.ts");
        var loader = BuildLoader(new ScriptCommandLoaderOptions
        {
            Directories = CmdDirectories,
            FailFastOnInvalidScript = true,
        });

        await Assert.ThrowsAnyAsync<Exception>(() => loader.LoadAndRegisterAsync(CancellationToken.None));
    }

    [Fact]
    public async Task First_script_wins_on_name_conflict()
    {
        // Alphabetical order: duplicate-name-a comes before duplicate-name-b.
        CopyFixture("duplicate-name-a.cmd.ts", "duplicate-name-a.cmd.ts");
        CopyFixture("duplicate-name-b.cmd.ts", "duplicate-name-b.cmd.ts");

        using var logFactory = new TestLogFactory();
        var loader = BuildLoader(new ScriptCommandLoaderOptions
        {
            Directories = CmdDirectories,
        }, logFactory);

        var (registry, summary) = await loader.LoadAndRegisterAsync(CancellationToken.None);

        Assert.Equal(1, registry.Count);
        Assert.Equal(1, summary.Conflicts);
        var winning = Assert.Single(registry.ScriptCommands);
        Assert.Equal("dup", winning.Name);
        Assert.Contains("/cmd/duplicate-name-a.cmd.ts", winning.SourceVirtualPath);
        Assert.Contains(logFactory.Records, r => r.Level == LogLevel.Warning && r.Message.Contains("duplicate-name-b"));
    }

    [Fact]
    public async Task MaxScripts_caps_discovery()
    {
        // Produce 3 distinct valid fixtures by templating.
        for (var i = 0; i < 3; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_tempDir, $"cmd-{i}.cmd.ts"), $$"""
                defineCommand({
                  name: "cmd{{i}}",
                  description: "Number {{i}}.",
                  handler: function () { }
                });
            """, TestContext.Current.CancellationToken);
        }

        using var logFactory = new TestLogFactory();
        var loader = BuildLoader(new ScriptCommandLoaderOptions
        {
            Directories = CmdDirectories,
            MaxScripts = 2,
        }, logFactory);

        var (registry, _) = await loader.LoadAndRegisterAsync(CancellationToken.None);
        Assert.Equal(2, registry.Count);
        Assert.Contains(logFactory.Records, r => r.Level == LogLevel.Warning && r.Message.Contains("MaxScripts"));
    }

    [Fact]
    public async Task Returns_empty_when_directories_have_no_matches()
    {
        // Create a non-matching file.
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "irrelevant", TestContext.Current.CancellationToken);

        var loader = BuildLoader(new ScriptCommandLoaderOptions
        {
            Directories = CmdDirectories,
        });
        var (registry, summary) = await loader.LoadAndRegisterAsync(CancellationToken.None);
        Assert.Equal(0, registry.Count);
        Assert.Equal(0, summary.Loaded);
    }

    // ── tiny in-memory log collector ─────────────────────────────────────────────
    private sealed class TestLogFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Records { get; } = new();
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public ILogger CreateLogger(string categoryName) => new TestLogger(Records);
    }

    private sealed class TestLogger : ILogger
    {
        private readonly List<(LogLevel, string)> _sink;
        public TestLogger(List<(LogLevel, string)> sink) { _sink = sink; }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_sink) _sink.Add((logLevel, formatter(state, exception)));
        }
        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}

internal static class ImmutableArrayExtensions
{
    public static System.Collections.Immutable.ImmutableArray<T> ToImmutableArray<T>(this T[] src)
        => System.Collections.Immutable.ImmutableArray.Create(src);
}
