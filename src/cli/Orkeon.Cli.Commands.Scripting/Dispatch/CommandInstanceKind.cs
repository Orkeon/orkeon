namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>Whether a dispatched command instance came from a sync or async command.</summary>
public enum CommandInstanceKind
{
    /// <summary>Produced by <c>defineCommand</c> via <c>commands.request(...)</c> (awaited inline).</summary>
    Sync,

    /// <summary>Produced by <c>defineAsyncCommand</c> via <c>commands.post(...)</c> (detached).</summary>
    Async,
}

/// <summary>Helpers over <see cref="CommandInstanceKind"/>.</summary>
public static class CommandInstanceKindExtensions
{
    /// <summary>The lowercase token used in JS-facing views (<c>"sync"</c>/<c>"async"</c>).</summary>
    public static string ToToken(this CommandInstanceKind kind)
        => kind == CommandInstanceKind.Async ? "async" : "sync";
}
