using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Runs;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Runs;

namespace Orkeon.Hosting.Runs;

/// <summary>The recording side and the reading side of one configured run history.</summary>
/// <param name="Sink">Where the events of a run are written.</param>
/// <param name="Reader">Where they are read back.</param>
/// <param name="Owned">What closing the history releases (the Sqlite connection), or null.</param>
internal sealed record ConfiguredRunHistory(IRunEventSink Sink, IRunHistoryReader Reader, IDisposable? Owned)
{
    /// <summary>Releases the store. A host never does it: the history outlives the host (see <see cref="RunHistoryServices"/>).</summary>
    public void Close() => Owned?.Dispose();
}

/// <summary>
/// <c>Orkeon:RunHistory</c> in a runner host: the section is bound, validated and declared; when it is enabled the host
/// holds a <see cref="ConfiguredRunHistory"/> and an <see cref="IRunHistoryReader"/> (what <c>orkeon runs</c> reads), and
/// <see cref="Activate"/> joins the history to the run being published. The store is infrastructure: it writes under
/// <c>/runs</c>, an internal mount no agent addresses, so it takes the privileged view of the file system.
/// </summary>
internal static class RunHistoryServices
{
    internal static void Register(HostBuilderContext context, IServiceCollection services)
    {
        services.AddOptions<RunHistoryOptions>()
            .Bind(context.Configuration.GetSection(RunHistoryOptions.SectionName))
            .ValidateSettings((o, _) => ProblemsOf(o))
            .DeclareSettings(RunHistoryOptions.SectionName);

        if (!context.Configuration.GetValue("Orkeon:RunHistory:Enabled", defaultValue: false))
            return;

        services.AddSingleton(sp => Create(sp));
        services.AddSingleton(sp => sp.GetRequiredService<ConfiguredRunHistory>().Reader);
    }

    /// <summary>
    /// Joins the history to the run being published, once the host exists (its settings were not known when
    /// <c>run.started</c> was written, so the events so far are replayed), and purges what retention no longer keeps.
    /// A host that publishes no run (or keeps no history) is left alone.
    /// </summary>
    internal static void Activate(IServiceProvider services)
    {
        var store = services.GetService<ConfiguredRunHistory>();
        if (store is null)
            return;

        // The writing side purges, when it first writes; a host that only reads the history (orkeon runs) never deletes anything.
#pragma warning disable CA2012, VSTHRD002 // Called once, while the host is built, before any event can race it.
        if (services.GetService<RunEventPublisher>() is { } publisher)
        {
            publisher.AttachAsync(store.Sink).GetAwaiter().GetResult();
            publisher.CompleteConfiguration();
        }
#pragma warning restore CA2012, VSTHRD002
    }

    private static ConfiguredRunHistory Create(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<RunHistoryOptions>>().Value;
        var fs = OwnFileSystem(sp, options);
        var time = sp.GetService<TimeProvider>() ?? TimeProvider.System;

        if (options.Store == Orkeon.Application.Runs.RunHistoryStore.Sqlite)
        {
#pragma warning disable CA2000 // The store outlives the host and is released with the process, or by Close.
            var sqlite = new SqliteRunEventSink(fs, options, time);
#pragma warning restore CA2000
            return new ConfiguredRunHistory(new RedactingRunEventSink(sqlite), sqlite, sqlite);
        }

        var sink = new JsonLinesFileRunEventSink(fs, options, time);
        return new ConfiguredRunHistory(new RedactingRunEventSink(sink), new JsonLinesRunHistoryReader(fs, options), null);
    }

    /// <summary>
    /// A file system over the history's own mount, with a registry of its own. Two things rule out the host's: the
    /// closing event of <c>orkeon run</c> is written after the host was disposed (the exit code is only known then, and
    /// the host's registry is disposed with it), and a hosted crew that has mounts of its own runs in a mount namespace
    /// that replaces the host's, <c>/runs</c> included. The store is infrastructure: the view is privileged (<c>/runs</c>
    /// is an internal mount no agent addresses) and the validator is the host's own.
    /// </summary>
    private static IFileSystemService OwnFileSystem(IServiceProvider sp, RunHistoryOptions options)
    {
        var registry = sp.GetService<FileSystemRegistry>();
        var mounts = registry?.GetMounts()
            .Where(mount => options.Path == mount.VirtualPath
                || options.Path.StartsWith(mount.VirtualPath.TrimEnd('/') + "/", StringComparison.Ordinal))
            .ToList();
        if (mounts is not { Count: > 0 } || sp.GetService<IPathValidator>() is not { } validator)
            return sp.GetService<PrivilegedFileSystemAccess>()?.FileSystem ?? sp.GetRequiredService<IFileSystemService>();

#pragma warning disable CA2000 // The registry is the store's own and lives with it: it must survive the host.
        // EXCEPTION-BOOTSTRAP: the history store provisions its own view of the /runs mount, outside the host's registry (it
        // is disposed before the closing event is written, and a hosted crew's mount namespace replaces it).
        return new FileSystemService(
            new FileSystemRegistry(mounts),
            validator,
            sp.GetService<ILoggerFactory>()?.CreateLogger<FileSystemService>() ?? NullLogger<FileSystemService>.Instance,
            scope: null,
            internalAccess: true);
#pragma warning restore CA2000
    }

    private static IEnumerable<string> ProblemsOf(RunHistoryOptions o)
    {
        if (!o.Path.StartsWith('/'))
            yield return $"{RunHistoryOptions.SectionName}:Path is '{o.Path}'; it is a virtual path (it starts with '/'), by default {RunHistoryOptions.DefaultPath}.";

        if (o.RetentionDays < 0)
            yield return $"{RunHistoryOptions.SectionName}:RetentionDays is {o.RetentionDays}; it is a number of days, 0 to keep every run.";

        if (o.MaxRuns < 0)
            yield return $"{RunHistoryOptions.SectionName}:MaxRuns is {o.MaxRuns}; it is a number of runs, 0 to keep every run.";
    }
}
