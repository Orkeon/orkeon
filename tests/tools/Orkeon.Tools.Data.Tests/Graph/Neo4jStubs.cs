using System.Reflection;
using Neo4j.Driver;

namespace Orkeon.Tools.Data.Tests.Graph;

/// <summary>
/// Shared <see cref="IDriver"/>/<see cref="IAsyncSession"/>/<see cref="IResultCursor"/> stubs for the Graph tool tests.
/// Built on top of <see cref="DispatchProxy"/> so we don't have to enumerate the
/// dozens of interface members that <see cref="Neo4j.Driver"/> ships (and that
/// vary across versions).
/// </summary>
internal static class Neo4jStubs
{
    /// <summary>Creates an <see cref="IDriver"/> that hands back the given session/counters.</summary>
    public static IDriver CreateDriver(SessionState state)
    {
        var driver = DispatchProxy.Create<IDriver, DriverProxy>();
        ((DriverProxy)(object)driver).Init(state);
        return driver;
    }

    /// <summary>Mutable state container reused by tests.</summary>
    public sealed class SessionState
    {
        public Func<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>? RecordsForQuery { get; set; }
        public int NodesCreated { get; set; }
        public int RelationshipsCreated { get; set; }
        public int PropertiesSet { get; set; }
    }

    public class DriverProxy : DispatchProxy
    {
        private SessionState _state = null!;
        public void Init(SessionState s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "AsyncSession":
                {
                    var sessionProxy = DispatchProxy.Create<IAsyncSession, AsyncSessionProxy>();
                    ((AsyncSessionProxy)(object)sessionProxy).Init(_state);
                    return sessionProxy;
                }
                case "Dispose": return null;
                case "DisposeAsync": return ValueTask.CompletedTask;
                case "CloseAsync": return Task.CompletedTask;
                default: return ProxyDefaults.For(targetMethod);
            }
        }
    }

    public class AsyncSessionProxy : DispatchProxy
    {
        private SessionState _state = null!;
        public void Init(SessionState s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "RunAsync" && args is { Length: >= 1 } && args[0] is string query)
            {
                var records = _state.RecordsForQuery is null
                    ? Array.Empty<IReadOnlyDictionary<string, object?>>()
                    : _state.RecordsForQuery(query);
                var cursor = DispatchProxy.Create<IResultCursor, ResultCursorProxy>();
                ((ResultCursorProxy)(object)cursor).Init(records, _state);
                return Task.FromResult(cursor);
            }
            if (targetMethod?.Name == "CloseAsync") return Task.CompletedTask;
            if (targetMethod?.Name == "DisposeAsync") return ValueTask.CompletedTask;
            if (targetMethod?.Name == "Dispose") return null;
            return ProxyDefaults.For(targetMethod);
        }
    }

    public class ResultCursorProxy : DispatchProxy
    {
        private IReadOnlyList<IReadOnlyDictionary<string, object?>> _records = null!;
        private SessionState _state = null!;
        private int _index = -1;

        public void Init(IReadOnlyList<IReadOnlyDictionary<string, object?>> r, SessionState s)
        {
            _records = r;
            _state = s;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "FetchAsync":
                    _index++;
                    return Task.FromResult(_index < _records.Count);
                case "ConsumeAsync":
                {
                    var summary = DispatchProxy.Create<IResultSummary, ResultSummaryProxy>();
                    ((ResultSummaryProxy)(object)summary).Init(_state);
                    return Task.FromResult(summary);
                }
                case "get_Current":
                {
                    var record = DispatchProxy.Create<IRecord, RecordProxy>();
                    ((RecordProxy)(object)record).Init(_records[_index]);
                    return record;
                }
                default: return ProxyDefaults.For(targetMethod);
            }
        }
    }

    public class ResultSummaryProxy : DispatchProxy
    {
        private SessionState _state = null!;
        public void Init(SessionState s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Counters")
            {
                var counters = DispatchProxy.Create<ICounters, CountersProxy>();
                ((CountersProxy)(object)counters).Init(_state);
                return counters;
            }
            return ProxyDefaults.For(targetMethod);
        }
    }

    public class CountersProxy : DispatchProxy
    {
        private SessionState _state = null!;
        public void Init(SessionState s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_NodesCreated": return _state.NodesCreated;
                case "get_RelationshipsCreated": return _state.RelationshipsCreated;
                case "get_PropertiesSet": return _state.PropertiesSet;
                default: return ProxyDefaults.For(targetMethod);
            }
        }
    }

    public class RecordProxy : DispatchProxy
    {
        private IReadOnlyDictionary<string, object?> _record = null!;
        public void Init(IReadOnlyDictionary<string, object?> r) => _record = r;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Values")
                return _record.ToDictionary(kv => kv.Key, kv => kv.Value!) as IReadOnlyDictionary<string, object>;
            if (targetMethod?.Name == "get_Keys")
                return _record.Keys.ToList() as IReadOnlyList<string>;
            if (targetMethod?.Name == "get_Item" && args is { Length: 1 } && args[0] is string key)
                return _record.TryGetValue(key, out var v) ? v : null;
            return ProxyDefaults.For(targetMethod);
        }
    }

    private static class ProxyDefaults
    {
        public static object? For(MethodInfo? m)
        {
            if (m is null) return null;
            if (m.ReturnType == typeof(void)) return null;
            if (m.ReturnType == typeof(Task)) return Task.CompletedTask;
            if (m.ReturnType == typeof(ValueTask)) return ValueTask.CompletedTask;
            if (m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = m.ReturnType.GetGenericArguments()[0];
                var def = inner.IsValueType && Nullable.GetUnderlyingType(inner) is null ? Activator.CreateInstance(inner) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [def]);
            }
            if (!m.ReturnType.IsValueType || Nullable.GetUnderlyingType(m.ReturnType) is not null) return null;
            return Activator.CreateInstance(m.ReturnType);
        }
    }
}
