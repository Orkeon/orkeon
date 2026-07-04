using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Memory.Sqlite;

/// <summary>
/// Source-generated logging delegates for <see cref="SqliteMemoryProvider"/>
/// (mirrors the LanceDb logging-partial convention).
/// </summary>
public sealed partial class SqliteMemoryProvider
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "SQLite memory database initialized (table: {TableName})")]
    private partial void LogDatabaseInitialized(string tableName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored memory item with key: {Key}")]
    private partial void LogStoredMemoryItem(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrieved memory item with key: {Key}")]
    private partial void LogRetrievedMemoryItem(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Memory item not found with key: {Key}")]
    private partial void LogMemoryItemNotFound(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated memory item with key: {Key}")]
    private partial void LogUpdatedMemoryItem(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cannot update non-existent memory item with key: {Key}")]
    private partial void LogCannotUpdateNonExistent(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted memory item with key: {Key}, Success: {Success}")]
    private partial void LogDeletedMemoryItem(string key, bool success);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared {Count} memory items")]
    private partial void LogClearedMemoryItems(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Counted {Count} memory items")]
    private partial void LogCountedMemoryItems(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} memory keys (skip: {Skip}, take: {Take})")]
    private partial void LogListedKeys(int count, int skip, int take);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} memory items matching query: {Query}")]
    private partial void LogFoundMemoryItems(int count, string query);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vector search found {Count} items above min score {MinScore} (topK={TopK})")]
    private partial void LogVectorSearchResults(int count, float minScore, int topK);
}
