using System.Reflection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Orkeon.Tools.Data.Tests.MongoDB;

/// <summary>
/// Shared <see cref="IMongoClient"/>/<see cref="IMongoDatabase"/>/<see cref="IMongoCollection{TDocument}"/>
/// stubs for the MongoDB tool tests, built on <see cref="DispatchProxy"/> to avoid
/// enumerating the dozens of interface members that the Mongo driver ships.
/// </summary>
internal static class MongoStubs
{
    public sealed class State
    {
        public List<BsonDocument> FindResult { get; set; } = new();
        public long CountResult { get; set; }
        public long? UpdateMatched { get; set; }
        public long? UpdateModified { get; set; }
        public long? DeletedCount { get; set; }
        public bool InsertCalled { get; set; }
        public List<string> CollectionNames { get; set; } = new();
        public List<BsonDocument> IndexesResult { get; set; } = new();
    }

    /// <summary>Builds a standalone collection stub for unit-testing helpers (e.g. InferFields).</summary>
    public static IMongoCollection<BsonDocument> CreateCollection(List<BsonDocument> docs)
    {
        var state = new State { FindResult = docs };
        var coll = DispatchProxy.Create<IMongoCollection<BsonDocument>, CollectionProxy>();
        ((CollectionProxy)(object)coll).Init(state);
        return coll;
    }

    public static IMongoClient CreateClient(State state)
    {
        var client = DispatchProxy.Create<IMongoClient, ClientProxy>();
        ((ClientProxy)(object)client).Init(state);
        return client;
    }

    public class ClientProxy : DispatchProxy
    {
        private State _state = null!;
        public void Init(State s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetDatabase")
            {
                var db = DispatchProxy.Create<IMongoDatabase, DatabaseProxy>();
                ((DatabaseProxy)(object)db).Init(_state);
                return db;
            }
            return ProxyDefaults.For(targetMethod);
        }
    }

    public class DatabaseProxy : DispatchProxy
    {
        private State _state = null!;
        public void Init(State s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetCollection" && targetMethod.IsGenericMethod)
            {
                var docType = targetMethod.GetGenericArguments()[0];
                if (docType == typeof(BsonDocument))
                {
                    var coll = DispatchProxy.Create<IMongoCollection<BsonDocument>, CollectionProxy>();
                    ((CollectionProxy)(object)coll).Init(_state);
                    return coll;
                }
                throw new NotSupportedException($"MongoStubs only supports BsonDocument collections, got {docType}.");
            }
            if (targetMethod?.Name == "ListCollectionNamesAsync")
            {
                var cursor = DispatchProxy.Create<IAsyncCursor<string>, StringCursorProxy>();
                ((StringCursorProxy)(object)cursor).Init(_state.CollectionNames);
                return Task.FromResult(cursor);
            }
            return ProxyDefaults.For(targetMethod);
        }
    }

    public class StringCursorProxy : DispatchProxy
    {
        private List<string> _items = new();
        private bool _moved;
        public void Init(List<string> items) => _items = items;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "MoveNext":
                    if (!_moved) { _moved = true; return _items.Count > 0; }
                    return false;
                case "MoveNextAsync":
                    if (!_moved) { _moved = true; return Task.FromResult(_items.Count > 0); }
                    return Task.FromResult(false);
                case "get_Current":
                    return _items;
                case "Dispose": return null;
                case "DisposeAsync": return ValueTask.CompletedTask;
                default: return ProxyDefaults.For(targetMethod);
            }
        }
    }

    public class IndexManagerProxy : DispatchProxy
    {
        private State _state = null!;
        public void Init(State s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "ListAsync")
            {
                var cursor = DispatchProxy.Create<IAsyncCursor<BsonDocument>, AsyncCursorProxy>();
                ((AsyncCursorProxy)(object)cursor).Init(_state.IndexesResult);
                return Task.FromResult(cursor);
            }
            return ProxyDefaults.For(targetMethod);
        }
    }

    public class CollectionProxy : DispatchProxy
    {
        private State _state = null!;
        public void Init(State s) => _state = s;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "FindAsync":
                {
                    var cursor = DispatchProxy.Create<IAsyncCursor<BsonDocument>, AsyncCursorProxy>();
                    ((AsyncCursorProxy)(object)cursor).Init(_state.FindResult);
                    return Task.FromResult(cursor);
                }
                case "CountDocumentsAsync":
                    return Task.FromResult(_state.CountResult);
                case "InsertOneAsync":
                    _state.InsertCalled = true;
                    if (args is { Length: >= 1 } && args[0] is BsonDocument bd && !bd.Contains("_id"))
                        bd["_id"] = ObjectId.GenerateNewId();
                    return Task.CompletedTask;
                case "UpdateOneAsync":
                {
                    UpdateResult ur = new StubUpdateResult(_state.UpdateMatched ?? 0L, _state.UpdateModified ?? 0L);
                    return Task.FromResult(ur);
                }
                case "DeleteOneAsync":
                {
                    DeleteResult dr = new StubDeleteResult(_state.DeletedCount ?? 0L);
                    return Task.FromResult(dr);
                }
                case "AggregateAsync":
                {
                    var cursor = DispatchProxy.Create<IAsyncCursor<BsonDocument>, AsyncCursorProxy>();
                    ((AsyncCursorProxy)(object)cursor).Init(_state.FindResult);
                    return Task.FromResult(cursor);
                }
                case "get_Indexes":
                {
                    var idx = DispatchProxy.Create<IMongoIndexManager<BsonDocument>, IndexManagerProxy>();
                    ((IndexManagerProxy)(object)idx).Init(_state);
                    return idx;
                }
                default:
                    return ProxyDefaults.For(targetMethod);
            }
        }
    }

    public class AsyncCursorProxy : DispatchProxy
    {
        private List<BsonDocument> _docs = new();
        private bool _moved;
        public void Init(List<BsonDocument> docs) => _docs = docs;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "MoveNext":
                    if (!_moved) { _moved = true; return _docs.Count > 0; }
                    return false;
                case "MoveNextAsync":
                    if (!_moved) { _moved = true; return Task.FromResult(_docs.Count > 0); }
                    return Task.FromResult(false);
                case "get_Current":
                    return _docs;
                case "Dispose": return null;
                case "DisposeAsync": return ValueTask.CompletedTask;
                default: return ProxyDefaults.For(targetMethod);
            }
        }
    }

    private sealed class StubUpdateResult : UpdateResult
    {
        public StubUpdateResult(long matched, long modified)
        {
            MatchedCount = matched;
            ModifiedCount = modified;
        }
        public override long MatchedCount { get; }
        public override long ModifiedCount { get; }
        public override bool IsAcknowledged => true;
        public override bool IsModifiedCountAvailable => true;
        public override BsonValue UpsertedId => BsonNull.Value;
    }

    private sealed class StubDeleteResult : DeleteResult
    {
        public StubDeleteResult(long deleted) { DeletedCount = deleted; }
        public override long DeletedCount { get; }
        public override bool IsAcknowledged => true;
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
