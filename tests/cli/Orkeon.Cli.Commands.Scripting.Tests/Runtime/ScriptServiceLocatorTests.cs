using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

public sealed partial class ScriptServiceLocatorTests
{
    private sealed record DummyService(string Tag);

    private static (ScriptServiceLocator Locator, ServiceProvider Services) Build(
        Action<ScriptServiceWhitelist> configure,
        Action<ServiceCollection>? configureServices = null)
    {
        var sc = new ServiceCollection();
        configureServices?.Invoke(sc);
        var sp = sc.BuildServiceProvider();
        var builder = new ScriptServiceWhitelist();
        configure(builder);
        return (new ScriptServiceLocator(builder.Build(), sp), sp);
    }

    [Fact]
    public void Get_returns_resolved_service()
    {
        var (locator, _) = Build(
            wl => wl.Add("dummy", sp => sp.GetRequiredService<DummyService>()),
            sc => sc.AddSingleton(new DummyService("hello")));

        Assert.True(locator.has("dummy"));
        var result = Assert.IsType<DummyService>(locator.get("dummy"));
        Assert.Equal("hello", result.Tag);
    }

    [Fact]
    public void Get_throws_with_listing_for_unknown_key()
    {
        var (locator, _) = Build(wl => wl.Add("dummy", sp => new DummyService("x")));

        Assert.False(locator.has("missing"));
        var ex = Assert.Throws<InvalidOperationException>(() => locator.get("missing"));
        Assert.Contains("missing", ex.Message);
        Assert.Contains("dummy", ex.Message); // available keys listed
    }

    [Fact]
    public void Optional_service_reports_false_when_unregistered()
    {
        var (locator, _) = Build(wl => wl.AddOptional("maybe", sp => sp.GetService<DummyService>()));

        Assert.False(locator.has("maybe"));
        Assert.Throws<InvalidOperationException>(() => locator.get("maybe"));
    }

    [Fact]
    public void Optional_service_reports_true_when_registered()
    {
        var (locator, _) = Build(
            wl => wl.AddOptional("maybe", sp => sp.GetService<DummyService>()),
            sc => sc.AddSingleton(new DummyService("ok")));
        Assert.True(locator.has("maybe"));
        Assert.IsType<DummyService>(locator.get("maybe"));
    }

    [Fact]
    public void Empty_locator_rejects_all_keys()
    {
        Assert.False(ScriptServiceLocator.Empty.has("anything"));
        Assert.Throws<InvalidOperationException>(() => ScriptServiceLocator.Empty.get("anything"));
    }

    [Fact]
    public void Approvals_entry_reads_the_watch_at_each_list()
    {
        var watch = new Orkeon.Cli.Abstractions.Tests.Doubles.FakeToolApprovalWatch();
        var (locator, sp) = Build(
            // The default whitelist's resolver, served under its key by this locator.
            wl => wl.AddOptional(
                ScriptServiceKeys.Approvals,
                DefaultScriptServiceWhitelist.Build().Build().Entries[ScriptServiceKeys.Approvals]),
            sc => sc.AddSingleton<Orkeon.Cli.Abstractions.Runners.IToolApprovalWatch>(watch));
        using var _ = sp;

        var approvals = Assert.IsType<ScriptPendingApprovals>(locator.get(ScriptServiceKeys.Approvals));
        Assert.Empty(approvals.list());

        watch.Pending.Add(new Orkeon.Cli.Abstractions.Runners.PendingToolApproval("read", new string('x', 80), null, null));
        var approval = Assert.Single(approvals.list());
        Assert.Null(approval.taskId);
        Assert.Null(approval.command);
        Assert.Equal("read", approval.tool);
        Assert.Equal(new string('x', 60) + "…", approval.input);
    }

    [Fact]
    [Trait("BR", "CH-198")]
    public void Approvals_typings_declare_the_shape_the_runtime_serves()
    {
        var typings = CliTypings();

        // The key the typings name is the key the whitelist serves, and it returns PendingApprovals.
        Assert.Contains($"get(name: \"{ScriptServiceKeys.Approvals}\"): PendingApprovals;", typings, StringComparison.Ordinal);

        // PendingApprovals.list() is the one member of ScriptPendingApprovals a script can call.
        Assert.Matches(@"\blist\(\)\s*:\s*readonly PendingApproval\[\]", DeclarationBody(typings, "PendingApprovals"));
        var list = typeof(ScriptPendingApprovals).GetMethod(nameof(ScriptPendingApprovals.list));
        Assert.NotNull(list);
        Assert.Empty(list.GetParameters());
        Assert.Equal(typeof(ScriptPendingApproval[]), list.ReturnType);

        // PendingApproval declares exactly the properties ScriptPendingApproval exposes, nullable where they are.
        var declared = ReadonlyField().Matches(DeclarationBody(typings, "PendingApproval"))
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);
        var nullability = new System.Reflection.NullabilityInfoContext();
        var runtime = typeof(ScriptPendingApproval)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .ToDictionary(
                p => p.Name,
                p => nullability.Create(p).ReadState == System.Reflection.NullabilityState.Nullable ? "string | null" : "string",
                StringComparer.Ordinal);
        Assert.Equal(runtime.OrderBy(kv => kv.Key, StringComparer.Ordinal), declared.OrderBy(kv => kv.Key, StringComparer.Ordinal));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"readonly (\w+): ([^;]+);")]
    private static partial System.Text.RegularExpressions.Regex ReadonlyField();

    private static string DeclarationBody(string typings, string name)
    {
        var start = typings.IndexOf($"interface {name} ", StringComparison.Ordinal);
        Assert.True(start >= 0, $"interface {name} is not declared in orkeon-cli.d.ts");
        return typings[start..typings.IndexOf("\n}", start, StringComparison.Ordinal)];
    }

    private static string CliTypings()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "cli", "Orkeon.Cli.Commands.Scripting", "Typings", "orkeon-cli.d.ts");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException("orkeon-cli.d.ts not found above the test base directory.");
    }
}
