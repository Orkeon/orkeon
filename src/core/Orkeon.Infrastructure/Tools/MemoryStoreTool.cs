using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="MemoryStoreTool"/>.</summary>
public sealed class MemoryStoreRequest
{
    /// <summary>Operation: list / add / replace / delete / get.</summary>
    [JsonPropertyName("operation")]
    [FieldSchema(Description = "Operation: list (entries, each with id, category, content and created_at), add (returns the new id), replace (retires a stale fact in place: same id and category, new content and created_at), delete, get.", Example = "add", IsRequired = false)]
    public string Operation { get; set; } = "list";

    /// <summary>Category filter / target (user / project / feedback / reference).</summary>
    [JsonPropertyName("category")]
    [FieldSchema(Description = "Category of the entry for add, filter for list (user / project / feedback / reference); add defaults to user, list to every category.", Example = "project", IsRequired = false)]
    public string? Category { get; set; }

    /// <summary>Content for add and replace.</summary>
    [JsonPropertyName("content")]
    [FieldSchema(Description = "The fact to keep, for add and replace.", Example = "The repository uses pnpm, never npm.", IsRequired = false)]
    public string? Content { get; set; }

    /// <summary>Entry id for get / replace / delete.</summary>
    [JsonPropertyName("id")]
    [FieldSchema(Description = "Entry id for get, replace and delete: what add returned.", Example = "3", IsRequired = false)]
    public int Id { get; set; }

    /// <summary>For list: newest entries first (created_at descending, then id descending); default insertion order.</summary>
    [JsonPropertyName("newest_first")]
    [FieldSchema(Description = "For list: true orders the entries newest first (created_at descending, then id descending); false, the default, keeps the order they were added in.", Example = "true", IsRequired = false)]
    public bool NewestFirst { get; set; }
}

/// <summary>Typed response for <see cref="MemoryStoreTool"/>.</summary>
public sealed class MemoryStoreResponse
{
    /// <summary>Operation outcome.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Entries returned by list; each carries id, category, content and created_at.</summary>
    [JsonPropertyName("entries")]
    public IReadOnlyList<object>? Entries { get; set; }

    /// <summary>Single entry returned by get.</summary>
    [JsonPropertyName("entry")]
    public object? Entry { get; set; }

    /// <summary>New / affected entry id.</summary>
    [JsonPropertyName("id")]
    public int? Id { get; set; }

    /// <summary>Failure reason when not successful.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Typed CRUD over the four memory categories (backs <c>/memory</c>).
/// Operations: <c>list</c> / <c>add</c> / <c>replace</c> / <c>delete</c> / <c>get</c>. The store behind it outlives the
/// process when <c>Orkeon:Cli:Memory:Persist</c> is on (<see cref="Memory.FileCategoryMemoryStore"/>).
/// </summary>
public sealed class MemoryStoreTool : ToolBase<MemoryStoreRequest, MemoryStoreResponse>
{
    /// <inheritdoc />
    public override string Name => "memory_store";
    /// <inheritdoc />
    public override string Description =>
        "List, add, replace, delete, and get typed memory entries (user/project/feedback/reference). " +
        "add returns the entry's id; every entry carries created_at; replace retires a stale fact in place, keeping its id; " +
        "list takes newest_first.";

    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly ICategoryMemoryStore _store;

    /// <summary>Creates the tool with its backing service(s).</summary>
    public MemoryStoreTool(ICategoryMemoryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    protected override Task<MemoryStoreResponse> ExecuteTypedAsync(
        MemoryStoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<MemoryStoreResponse> ExecuteTypedCoreAsync()
        {
#pragma warning disable CA1308 // lowercase is the normalized switch subject for the operation keyword
            var op = (request.Operation ?? "list").Trim().ToLowerInvariant();
#pragma warning restore CA1308
            return op switch
            {
                "list" => await ListAsync(request, cancellationToken).ConfigureAwait(false),
                "add" => await AddAsync(request, cancellationToken).ConfigureAwait(false),
                "replace" => await ReplaceAsync(request, cancellationToken).ConfigureAwait(false),
                "delete" => await DeleteAsync(request, cancellationToken).ConfigureAwait(false),
                "get" => await GetAsync(request, cancellationToken).ConfigureAwait(false),
                _ => new MemoryStoreResponse { Success = false, Error = $"unknown operation '{op}'." },
            };
        }
    }

    private async Task<MemoryStoreResponse> ListAsync(MemoryStoreRequest request, CancellationToken cancellationToken)
    {
        var entries = await _store.ListAsync(request.Category, request.NewestFirst, cancellationToken).ConfigureAwait(false);
        return new MemoryStoreResponse { Success = true, Entries = entries.ToArray<object>() };
    }

    private async Task<MemoryStoreResponse> AddAsync(MemoryStoreRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return new MemoryStoreResponse { Success = false, Error = "content is required for add." };
        var id = await _store.AddAsync(request.Category ?? "user", request.Content, cancellationToken).ConfigureAwait(false);
        return new MemoryStoreResponse { Success = true, Id = id };
    }

    private async Task<MemoryStoreResponse> ReplaceAsync(MemoryStoreRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return new MemoryStoreResponse { Success = false, Error = "content is required for replace." };
        var replaced = await _store.ReplaceAsync(request.Id, request.Content, cancellationToken).ConfigureAwait(false);
        return new MemoryStoreResponse { Success = replaced, Id = request.Id, Error = replaced ? null : $"no entry with id {request.Id}." };
    }

    private async Task<MemoryStoreResponse> DeleteAsync(MemoryStoreRequest request, CancellationToken cancellationToken)
    {
        var ok = await _store.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
        return new MemoryStoreResponse { Success = ok, Id = request.Id, Error = ok ? null : $"no entry with id {request.Id}." };
    }

    private async Task<MemoryStoreResponse> GetAsync(MemoryStoreRequest request, CancellationToken cancellationToken)
    {
        var entry = await _store.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);
        return entry is null
            ? new MemoryStoreResponse { Success = false, Error = $"no entry with id {request.Id}." }
            : new MemoryStoreResponse { Success = true, Entry = entry };
    }
}
