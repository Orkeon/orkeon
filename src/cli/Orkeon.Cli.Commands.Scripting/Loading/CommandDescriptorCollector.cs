using Jint;
using Jint.Native;

namespace Orkeon.Cli.Commands.Scripting.Loading;

/// <summary>
/// Collects <see cref="CommandDescriptor"/> instances pushed from JS via the
/// <c>defineCommand</c> global. One collector instance per <c>*.cmd.ts</c> script.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: created → <see cref="Add"/> called N times during <c>engine.Evaluate(js)</c>
/// → <see cref="Freeze"/> called by the loader → any subsequent <see cref="Add"/> throws
/// <see cref="InvalidOperationException"/>. This enforces spec §15 risk 4: a script that
/// tries to register a command from inside a handler (post-load) fails loudly.
/// </para>
/// <para>
/// Not thread-safe. The runner invokes Jint engines serially (§6.3 spec).
/// </para>
/// </remarks>
public sealed class CommandDescriptorCollector
{
    private readonly string _sourceVirtualPath;
    private readonly List<CommandDescriptor> _descriptors = new();
    private bool _frozen;

    public CommandDescriptorCollector(string sourceVirtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVirtualPath);
        _sourceVirtualPath = sourceVirtualPath;
    }

    /// <summary>Descriptors collected so far, in declaration order.</summary>
    public IReadOnlyList<CommandDescriptor> Descriptors => _descriptors;

    /// <summary>True after <see cref="Freeze"/>; <see cref="Add"/> rejects further entries.</summary>
    public bool IsFrozen => _frozen;

    /// <summary>
    /// Called by the <c>defineCommand</c> JS binding (sync command). Throws
    /// <see cref="InvalidOperationException"/> after <see cref="Freeze"/> — surfaced to JS as a
    /// thrown exception, the "fail loudly" semantics demanded by spec §15.
    /// </summary>
    public void Add(JsValue descRaw)
    {
        var (obj, name, description, aliases, argsSchemaJson) = ParseCommon(descRaw, "defineCommand");

        var handler = obj.Get("handler");
        if (handler.IsUndefined() || handler.IsNull())
            throw new ArgumentException($"defineCommand({{name:'{name}'}}) in {_sourceVirtualPath}: 'handler' is required.");
        if (handler is not Jint.Native.Function.Function)
            throw new ArgumentException($"defineCommand({{name:'{name}'}}) in {_sourceVirtualPath}: 'handler' must be a function.");

        _descriptors.Add(new CommandDescriptor
        {
            SourceVirtualPath = _sourceVirtualPath,
            Name = name,
            Aliases = aliases,
            Description = description,
            Kind = CommandKind.Sync,
            Handler = handler,
            ArgsSchemaJson = argsSchemaJson,
        });
    }

    /// <summary>
    /// Called by the <c>defineAsyncCommand</c> JS binding (async command, design §4.2).
    /// Requires <c>dispatch</c>; <c>completed</c> is optional; <c>maxConcurrent</c>, when
    /// present, must be an integer ≥ 1 (design §8 item 7).
    /// </summary>
    public void AddAsync(JsValue descRaw)
    {
        var (obj, name, description, aliases, argsSchemaJson) = ParseCommon(descRaw, "defineAsyncCommand");

        var dispatch = obj.Get("dispatch");
        if (dispatch.IsUndefined() || dispatch.IsNull())
            throw new ArgumentException($"defineAsyncCommand({{name:'{name}'}}) in {_sourceVirtualPath}: 'dispatch' is required.");
        if (dispatch is not Jint.Native.Function.Function)
            throw new ArgumentException($"defineAsyncCommand({{name:'{name}'}}) in {_sourceVirtualPath}: 'dispatch' must be a function.");

        JsValue? completed = null;
        var completedVal = obj.Get("completed");
        if (!completedVal.IsUndefined() && !completedVal.IsNull())
        {
            if (completedVal is not Jint.Native.Function.Function)
                throw new ArgumentException($"defineAsyncCommand({{name:'{name}'}}) in {_sourceVirtualPath}: 'completed' must be a function.");
            completed = completedVal;
        }

        int? maxConcurrent = null;
        var maxVal = obj.Get("maxConcurrent");
        if (!maxVal.IsUndefined() && !maxVal.IsNull())
        {
            if (!maxVal.IsNumber())
                throw new ArgumentException($"defineAsyncCommand({{name:'{name}'}}) in {_sourceVirtualPath}: 'maxConcurrent' must be a number.");
            var raw = maxVal.AsNumber();
            if (raw < 1 || raw != Math.Floor(raw))
                throw new ArgumentException($"defineAsyncCommand({{name:'{name}'}}) in {_sourceVirtualPath}: 'maxConcurrent' must be an integer ≥ 1.");
            maxConcurrent = (int)raw;
        }

        _descriptors.Add(new CommandDescriptor
        {
            SourceVirtualPath = _sourceVirtualPath,
            Name = name,
            Aliases = aliases,
            Description = description,
            Kind = CommandKind.Async,
            Dispatch = dispatch,
            Completed = completed,
            MaxConcurrent = maxConcurrent,
            ArgsSchemaJson = argsSchemaJson,
        });
    }

    /// <summary>Parses the fields common to both command shapes: name, description, aliases, args.</summary>
    private (Jint.Native.Object.ObjectInstance Obj, string Name, string Description, System.Collections.Immutable.ImmutableArray<string> Aliases, JsValue? ArgsSchemaJson)
        ParseCommon(JsValue descRaw, string globalName)
    {
        if (_frozen)
            throw new InvalidOperationException(
                $"{globalName}() called from {_sourceVirtualPath} after the script load phase. " +
                "Scripted commands must be declared at module-top-level, not from inside handlers.");

        if (descRaw is null || descRaw.IsNull() || descRaw.IsUndefined() || !descRaw.IsObject())
            throw new ArgumentException(
                $"{globalName}({{...}}) in {_sourceVirtualPath} requires an object argument.",
                nameof(descRaw));

        var obj = descRaw.AsObject();

        var nameVal = obj.Get("name");
        if (!nameVal.IsString())
            throw new ArgumentException($"{globalName}({{...}}) in {_sourceVirtualPath}: 'name' must be a string.");
        var name = nameVal.AsString();

        var descriptionVal = obj.Get("description");
        if (!descriptionVal.IsString())
            throw new ArgumentException($"{globalName}({{name:'{name}'}}) in {_sourceVirtualPath}: 'description' must be a string.");
        var description = descriptionVal.AsString();

        var aliases = ParseAliases(obj, name, globalName);

        var argsRaw = obj.Get("args");
        JsValue? argsSchemaJson = argsRaw.IsUndefined() || argsRaw.IsNull() ? null : argsRaw;

        return (obj, name, description, aliases, argsSchemaJson);
    }

    /// <summary>Parses the optional <c>aliases</c> array, validating every element is a string.</summary>
    private System.Collections.Immutable.ImmutableArray<string> ParseAliases(
        Jint.Native.Object.ObjectInstance obj, string name, string globalName)
    {
        var aliasVal = obj.Get("aliases");
        if (aliasVal.IsUndefined() || aliasVal.IsNull())
            return System.Collections.Immutable.ImmutableArray<string>.Empty;

        if (!aliasVal.IsArray())
            throw new ArgumentException($"{globalName}({{name:'{name}'}}) in {_sourceVirtualPath}: 'aliases' must be an array of strings.");
        var arr = aliasVal.AsArray();
        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>((int)arr.Length);
        for (uint i = 0; i < arr.Length; i++)
        {
            var element = arr.Get(i);
            if (!element.IsString())
                throw new ArgumentException($"{globalName}({{name:'{name}'}}) in {_sourceVirtualPath}: alias #{i} must be a string.");
            builder.Add(element.AsString());
        }
        return builder.ToImmutable();
    }

    /// <summary>Locks the collector — subsequent <see cref="Add"/>/<see cref="AddAsync"/> calls throw.</summary>
    public void Freeze() => _frozen = true;
}
