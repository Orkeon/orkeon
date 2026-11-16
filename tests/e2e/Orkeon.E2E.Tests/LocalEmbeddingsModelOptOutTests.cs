namespace Orkeon.E2E.Tests;

/// <summary>
/// The local embedding model of the packed <c>Orkeon.Scripting</c>. Opted out (CH-01, CH-18, CH-19): a consumer that
/// sets <c>ExcludeAssets="build"</c> downloads nothing, builds without <c>LocalEmbeddingsModel/</c> in its output and
/// runs offline, the first local embedding failing with a clear message. Default reference offline (CH-02): the
/// upstream download target runs and fails because no network is reachable. Online (CH-45, Category=Integration):
/// a consumer that keeps the default reference downloads the model over the network, then embeds
/// (384 dimensions). Offline tests restore from a local feed and the repository's package cache.
/// Category=Slow: the pack builds the lineup.
/// </summary>
[Trait("Category", "Slow")]
public sealed class LocalEmbeddingsModelOptOutTests : IClassFixture<LocalEmbeddingsModelOptOutTests.PackFixture>
{
    private readonly PackFixture _fixture;

    /// <summary>Takes the shared pack and local feed.</summary>
    public LocalEmbeddingsModelOptOutTests(PackFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("BR", "CH-01")]
    public void ShouldBuildWithoutTheModel_WhenTheConsumerExcludesBuildAssets()
    {
        var result = BuildConsumer("optout", excludeBuild: true);

        Assert.True(result.BuildExit == 0, $"The opted-out consumer failed to build ({result.BuildExit}):\n{result.BuildOutput}");
        // The upstream targets copy the model to <OutDir>/LocalEmbeddingsModel/ (TargetPath), OutDir being bin/Release/net10.0:
        // the whole bin folder is searched, so the check holds whatever the target framework folder is.
        Assert.True(Directory.Exists(result.OutputDirectory), $"{result.OutputDirectory} was not produced by the build.");
        var found = Directory.GetDirectories(result.OutputDirectory, "LocalEmbeddingsModel", SearchOption.AllDirectories);
        Assert.True(found.Length == 0, $"LocalEmbeddingsModel/ was found: {string.Join(", ", found)}.");
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldRunTheModelDownload_WhenTheConsumerKeepsBuildAssets()
    {
        var result = BuildConsumer("default", excludeBuild: false);

        Assert.True(result.BuildExit != 0, $"The default consumer was expected to fail offline, but built:\n{result.BuildOutput}");
        Assert.True(
            result.BuildOutput.Contains("SmartComponents.LocalEmbeddings.targets", StringComparison.Ordinal)
            && result.BuildOutput.Contains("MSB3923", StringComparison.Ordinal),
            $"The upstream model download (MSB3923 in SmartComponents.LocalEmbeddings.targets) did not run:\n{result.BuildOutput}");
    }

    [Fact]
    [Trait("BR", "CH-18")]
    public void ShouldStartAndListTools_WhenTheModelIsExcluded()
    {
        var run = BuildAndRun("optout-run");

        Assert.True(run.Exit == 0, $"The opted-out host did not reach the end of the tool listing ({run.Exit}):\n{run.Output}");
        var line = run.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("TOOLS=", StringComparison.Ordinal));
        Assert.NotNull(line);
        Assert.True(int.Parse(line!["TOOLS=".Length..], System.Globalization.CultureInfo.InvariantCulture) > 0, $"No tool listed:\n{run.Output}");
        var names = run.Output.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("TOOLNAMES=", StringComparison.Ordinal));
        Assert.True(names.Contains("LocalEmbedTool", StringComparison.Ordinal), $"LocalEmbedTool was not constructed:\n{run.Output}");
        // A tool that needs a service this minimal container lacks (a secret provider, IEmbeddingService, an option class)
        // fails here for a reason unrelated to CH-18: a real host registers them. Those failures are printed and tolerated;
        // the invariant is that no construction or registration opened the model.
        var failures = run.Output.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("TOOLFAIL=", StringComparison.Ordinal) || l.StartsWith("REGFAIL=", StringComparison.Ordinal)).ToList();
        foreach (var marker in new[] { "LocalEmbeddingsModel", "model.onnx", "ExcludeAssets" })
            Assert.True(!failures.Any(f => f.Contains(marker, StringComparison.Ordinal)), $"A construction or registration opened the model ({marker}):\n{string.Join('\n', failures)}");
    }

    [Fact]
    [Trait("BR", "CH-18")]
    public void ShouldFailTheFirstLocalEmbeddingClearly_WhenTheModelIsExcluded()
    {
        var run = BuildAndRun("optout-embed");

        var line = run.Output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("EMBED=", StringComparison.Ordinal));
        Assert.True(line is not null, $"The consumer printed no embedding outcome ({run.Exit}):\n{run.Output}");
        Assert.True(line.Contains("LocalEmbeddingsModel/default/model.onnx", StringComparison.Ordinal), $"Missing model path in: {line}");
        Assert.True(line.Contains("ExcludeAssets=\"build\"", StringComparison.Ordinal), $"Missing ExcludeAssets hint in: {line}");
    }

    [Fact]
    [Trait("BR", "CH-19")]
    public void ShouldSkipTheModel_WhenEveryReferenceExcludesTheBuildAssets()
    {
        var result = BuildConsumer("optout-both", excludeBuild: true, localReference: " ExcludeAssets=\"build\"");

        Assert.True(result.BuildExit == 0, $"The consumer excluding both references failed to build ({result.BuildExit}):\n{result.BuildOutput}");
        Assert.True(Directory.Exists(result.OutputDirectory), $"{result.OutputDirectory} was not produced by the build.");
        var found = Directory.GetDirectories(result.OutputDirectory, "LocalEmbeddingsModel", SearchOption.AllDirectories);
        Assert.True(found.Length == 0, $"LocalEmbeddingsModel/ was found: {string.Join(", ", found)}.");
    }

    [Fact]
    [Trait("BR", "CH-19")]
    public void ShouldStillRunTheModelTargets_WhenOneReferenceKeepsTheBuildAssets()
    {
        var result = BuildConsumer("optout-mixed", excludeBuild: true, localReference: "");

        Assert.True(result.BuildExit != 0, $"The mixed consumer was expected to fail offline, but built:\n{result.BuildOutput}");
        Assert.True(
            result.BuildOutput.Contains("SmartComponents.LocalEmbeddings.targets", StringComparison.Ordinal)
            && result.BuildOutput.Contains("MSB3923", StringComparison.Ordinal),
            $"The upstream model download did not run:\n{result.BuildOutput}");
    }

    [Fact]
    [Trait("BR", "CH-45")]
    [Trait("Category", "Integration")]
    public async Task ShouldDownloadTheModelAndEmbed_WhenTheConsumerKeepsBuildAssetsOnline()
    {
        var targets = Directory.EnumerateFiles(_fixture.PackagesPath, "SmartComponents.LocalEmbeddings.targets", SearchOption.AllDirectories).FirstOrDefault();
        Assert.SkipWhen(targets is null, "SmartComponents.LocalEmbeddings.targets was not found in the package cache.");
        var text = await File.ReadAllTextAsync(targets!, TestContext.Current.CancellationToken);
        var element = text.IndexOf("<LocalEmbeddingsModelUrl", StringComparison.Ordinal);
        var start = element < 0 ? -1 : text.IndexOf("https://", element, StringComparison.Ordinal);
        Assert.SkipWhen(start < 0, "No LocalEmbeddingsModelUrl download host found in the upstream targets file.");
        var rest = text[start..];
        var hostEnd = rest.IndexOf('/', "https://".Length);
        var hostUrl = hostEnd < 0 ? rest : rest[..hostEnd];
        var reachable = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var request = new HttpRequestMessage(HttpMethod.Head, hostUrl);
            using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
            reachable = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Unreachable network: the test is skipped below, never failed.
        }

        Assert.SkipWhen(!reachable, $"The model download host {hostUrl} is unreachable.");

        var run = BuildAndRun("online", excludeBuild: false, online: true, dimensions: true);
        var outputDirectory = Path.Combine(_fixture.Scratch, "online", "bin");
        var model = Directory.GetFiles(outputDirectory, "model.onnx", SearchOption.AllDirectories)
            .FirstOrDefault(f => f.Replace('\\', '/').EndsWith("LocalEmbeddingsModel/default/model.onnx", StringComparison.Ordinal));
        Assert.True(model is not null, $"LocalEmbeddingsModel/default/model.onnx was not found under {outputDirectory}.");
        Assert.True(new FileInfo(model).Length > 0, $"{model} is empty.");
        Assert.True(run.Exit == 0, $"The consumer program exited {run.Exit}:\n{run.Output}");
        Assert.True(run.Output.Split('\n').Select(l => l.Trim()).Contains("DIMENSIONS=384"), $"DIMENSIONS=384 was not printed:\n{run.Output}");
    }

    // dimensions: the program only prints DIMENSIONS=<length of one local embedding> instead of the tool listing.
    private (int Exit, string Output) BuildAndRun(string name, bool excludeBuild = true, bool online = false, bool dimensions = false)
    {
        var build = BuildConsumer(name, excludeBuild, program: HostProgram, online: online);
        Assert.True(build.BuildExit == 0, $"The consumer failed to build ({build.BuildExit}):\n{build.BuildOutput}");
        List<string> runArgs = ["run", "--no-build", "-c", "Release", "--project", Path.Combine(_fixture.Scratch, name, "Consumer.csproj")];
        if (dimensions)
            runArgs.AddRange(["--", "--dimensions"]);
        return LineupPackFixture.Run("dotnet", runArgs, Path.Combine(_fixture.Scratch, name));
    }

    // Reflection keeps the program independent of the packed surface: it adds the public local embeddings registration,
    // instantiates every IBaseTool it can, prints TOOLS=<count>, then asks the local provider for one embedding.
    private const string HostProgram = """
        using System.Reflection;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Configuration;
        foreach (var f in Directory.GetFiles(AppContext.BaseDirectory, "Orkeon*.dll"))
            try { Assembly.LoadFrom(f); } catch (Exception) { }
        var types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); } }).ToList();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(types.First(t => t.FullName == "Orkeon.Domain.Tools.Security.IPathValidator"), types.First(t => t.FullName == "Orkeon.Infrastructure.Security.PathValidator"));
        var fsAdd = types.Where(t => t.IsAbstract && t.IsSealed).SelectMany(t => t.GetMethods()).First(m => m.Name == "AddOrkeonFileSystem");
        fsAdd.Invoke(null, [services, new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Orkeon:FileSystem:Mounts:0"] = AppContext.BaseDirectory.TrimEnd('/') + ":/workspace:rw" }).Build()]);
        var add = types.Where(t => t.IsAbstract && t.IsSealed).SelectMany(t => t.GetMethods()).First(m => m.Name == "AddOrkeonLocalEmbeddings");
        add.Invoke(null, add.GetParameters().Select((p, i) => i == 0 ? (object?)services : p.HasDefaultValue ? p.DefaultValue : null).ToArray());
        foreach (var extra in new[] { "AddOrkeonWebTools", "AddOrkeonCacheSearchTool", "AddOrkeonApplication" })
        {
            var m = types.Where(t => t.IsAbstract && t.IsSealed).SelectMany(t => t.GetMethods()).FirstOrDefault(x => x.Name == extra && x.GetParameters().Length >= 1 && x.GetParameters()[0].ParameterType == typeof(IServiceCollection));
            try { m?.Invoke(null, m.GetParameters().Select((p, i) => i == 0 ? (object?)services : p.HasDefaultValue ? p.DefaultValue : null).ToArray()); } catch (Exception ex) { Console.WriteLine("REGFAIL=" + extra + ": " + (ex.InnerException ?? ex).Message.Replace('\n', ' ')); }
        }
        using var sp = services.BuildServiceProvider();
        if (args.Contains("--dimensions"))
        {
            var embeddingProviderType = types.First(t => t.FullName == "Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider");
            var embeddingProvider = sp.GetRequiredService(embeddingProviderType);
            var embedMethod = embeddingProviderType.GetMethods().First(m => m.Name.Contains("Embed", StringComparison.Ordinal));
            var embedArgs = embedMethod.GetParameters().Select(p => p.ParameterType == typeof(string) ? "hello" : p.ParameterType.IsAssignableFrom(typeof(string[])) ? new[] { "hello" } : p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None : null).ToArray();
            var embedTask = (Task)embedMethod.Invoke(embeddingProvider, embedArgs)!;
            await embedTask;
            object? current = embedTask.GetType().GetProperty("Result")!.GetValue(embedTask);
            if (current is Array outer && outer.Length > 0 && outer.GetValue(0) is not float) current = outer.GetValue(0);
            else if (current is System.Collections.IEnumerable seq && current is not Array) current = seq.Cast<object>().FirstOrDefault();
            var length = current is Array vector ? vector.Length : current?.GetType().GetProperty("Length")?.GetValue(current) as int? ?? -1;
            Console.WriteLine("DIMENSIONS=" + length);
            return 0;
        }
        var toolType = types.First(t => t.Name == "IBaseTool");
        var names = new List<string>();
        foreach (var t in types.Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && toolType.IsAssignableFrom(t)))
            try { ActivatorUtilities.CreateInstance(sp, t); names.Add(t.Name); }
            catch (Exception ex) { Console.WriteLine("TOOLFAIL=" + t.Name + ": " + (ex.InnerException ?? ex).Message.Replace('\n', ' ')); }
        Console.WriteLine("TOOLS=" + names.Count);
        Console.WriteLine("TOOLNAMES=" + string.Join(",", names));
        var providerType = types.First(t => t.FullName == "Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider");
        try
        {
            var provider = sp.GetRequiredService(providerType);
            var method = providerType.GetMethods().First(m => m.Name.Contains("Embed", StringComparison.Ordinal));
            var margs = method.GetParameters().Select(p => p.ParameterType == typeof(string) ? "hello" : p.ParameterType.IsAssignableFrom(typeof(string[])) ? new[] { "hello" } : p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None : null).ToArray();
            var task = method.Invoke(provider, margs);
            if (task is Task tk) await tk;
            Console.WriteLine("EMBED=unexpected success");
        }
        catch (Exception ex)
        {
            while (ex is AggregateException or System.Reflection.TargetInvocationException && ex.InnerException is not null) ex = ex.InnerException;
            Console.WriteLine("EMBED=" + ex.Message.Replace('\n', ' '));
            var chain = new List<string>();
            for (Exception? c = ex; c is not null; c = c.InnerException) chain.Add(c.GetType().Name);
            Console.WriteLine("EMBEDCHAIN=" + string.Join(">", chain));
        }
        return 0;
        """;

    private (int BuildExit, string BuildOutput, string OutputDirectory) BuildConsumer(string name, bool excludeBuild, string? localReference = null, string program = "return 0;\n", bool online = false)
    {
        Assert.True(_fixture.PackExitCode == 0, $"Packing failed with exit code {_fixture.PackExitCode}:\n{_fixture.PackOutput}");

        var project = Path.Combine(_fixture.Scratch, name);
        var cache = Path.Combine(_fixture.Scratch, name + "-model-cache");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(cache);

        var exclude = excludeBuild ? " ExcludeAssets=\"build\"" : "";
        var csproj = Path.Combine(project, "Consumer.csproj");
        File.WriteAllText(csproj, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Orkeon.Scripting" Version="{{_fixture.Version}}"{{exclude}} />
                {{(localReference is null ? "" : $"<PackageReference Include=\"Orkeon.Tools.Embeddings.Local\" Version=\"{_fixture.Version}\"{localReference} />")}}
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(project, "Program.cs"), program);

        var (restoreExit, restoreOutput) = _fixture.Restore(csproj, project);
        Assert.True(restoreExit == 0, $"restore of {name} failed ({restoreExit}):\n{restoreOutput}");
        var stale = _fixture.FindStaleExtractions();
        Assert.True(stale.Count == 0, $"{name} restored packages this run did not pack: {string.Join(", ", stale)}.");

        List<string> buildArgs = ["build", csproj, "--no-restore", "-nologo", "-c", "Release", "-v:n"];
        if (!online)
        {
            buildArgs.Add("-p:LocalEmbeddingsModelUrl=http://127.0.0.1:9/none");
            buildArgs.Add("-p:LocalEmbeddingsVocabUrl=http://127.0.0.1:9/none");
        }

        buildArgs.Add($"-p:LocalEmbeddingsModelCacheDir={cache}{Path.DirectorySeparatorChar}");
        var (buildExit, buildOutput) = LineupPackFixture.Run("dotnet", buildArgs, project);
        return (buildExit, buildOutput, Path.Combine(project, "bin"));
    }

    /// <summary>
    /// Packs the lineup the consumer restores from into one local feed at the run version; removes the scratch folder and
    /// the cache folders of the run at the end.
    /// </summary>
    public sealed class PackFixture : LineupPackFixture
    {
        private static readonly string[] PackagedProjects =
        [
            "src/packaging/Orkeon/Orkeon.csproj",
            "src/packaging/Orkeon.Tools/Orkeon.Tools.csproj",
            "src/packaging/Orkeon.Tools.Embeddings.Local/Orkeon.Tools.Embeddings.Local.Package.csproj",
            "src/packaging/Orkeon.Scripting/Orkeon.Scripting.Package.csproj",
            "src/constants/Orkeon.Constants.Cli/Orkeon.Constants.Cli.csproj",
        ];

        /// <summary>Initializes the fixture: packs; failures are kept for the tests to assert on.</summary>
        public PackFixture()
            : base("orkeon-optout-", PackagedProjects)
        {
        }
    }
}
