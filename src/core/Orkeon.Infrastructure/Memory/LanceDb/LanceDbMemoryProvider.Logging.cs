using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// Source-generated logging delegates of <see cref="LanceDbMemoryProvider"/>.
/// </summary>
public partial class LanceDbMemoryProvider
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Stored memory item with key: {Key} in LanceDB")]
    private partial void LogStoredMemoryItemWithKey(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Memory item not found with key: {Key} in LanceDB")]
    private partial void LogMemoryItemNotFoundWith(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Retrieved memory item with key: {Key} from LanceDB")]
    private partial void LogRetrievedMemoryItemWithKey(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cannot update non-existent memory item with key: {Key} in LanceDB")]
    private partial void LogCannotUpdateNonExistentMemory(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated memory item with key: {Key} in LanceDB")]
    private partial void LogUpdatedMemoryItemWithKey(string key);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted memory item with key: {Key} from LanceDB, Success: {Success}")]
    private partial void LogDeletedMemoryItemWithKey(string key, bool success);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} memory items matching query: {Query} in LanceDB")]
    private partial void LogFoundMemoryItemsMatchingQuery(int count, string query);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared {Count} memory items from LanceDB")]
    private partial void LogClearedAllMemoryItems(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Counted {Count} memory items in LanceDB")]
    private partial void LogCountedMemoryItems(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} memory keys from LanceDB (skip: {Skip}, take: {Take})")]
    private partial void LogListedMemoryKeys(int count, int skip, int take);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ensured LanceDB table exists on the remote server: {TableName}")]
    private partial void LogEnsuredTableExists(string tableName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "LanceDB table {TableName} was created concurrently by another client; continuing")]
    private partial void LogTableCreationRaceLost(string tableName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created LanceDB full-text index on column {Column} of table {TableName}")]
    private partial void LogCreatedFullTextIndex(string column, string tableName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create the LanceDB full-text index on column {Column} of table {TableName}; SearchAsync will surface server errors until the index exists")]
    private partial void LogFullTextIndexCreationFailed(Exception exception, string column, string tableName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Vector search found {Count} items above min score {MinScore} (topK={TopK})")]
    private partial void LogVectorSearchResults(int count, float minScore, int topK);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hybrid search found {Count} items (topK={TopK})")]
    private partial void LogHybridSearchResults(int count, int topK);
}
