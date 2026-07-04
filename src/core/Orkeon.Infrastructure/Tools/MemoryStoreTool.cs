using System.Text.Json.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Infrastructure.Tools;

/// <summary>Typed request for <see cref="MemoryStoreTool"/>.</summary>
public sealed class MemoryStoreRequest
{
    /// <summary>Operation: list / add / delete / get.</summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = "list";

    /// <summary>Category filter / target (user / project / feedback / reference).</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Content for add.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>Entry id for get / delete.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

/// <summary>Typed response for <see cref="MemoryStoreTool"/>.</summary>
public sealed class MemoryStoreResponse
{
    /// <summary>Operation outcome.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Entries returned by list.</summary>
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
/// Typed CRUD over the four memory categories (exp 07 SPEC §7.3; backs <c>/memory</c>).
/// Operations: <c>list</c> / <c>add</c> / <c>delete</c> / <c>get</c>.
/// </summary>
public sealed class MemoryStoreTool : ToolBase<MemoryStoreRequest, MemoryStoreResponse>
{
    /// <inheritdoc />
    public override string Name => "memory_store";
    /// <inheritdoc />
    public override string Description =>
        "List, add, delete, and get typed memory entries (user/project/feedback/reference).";

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
            switch (op)
            {
                case "list":
                {
                    var entries = await _store.ListAsync(request.Category, cancellationToken).ConfigureAwait(false);
                    return new MemoryStoreResponse { Success = true, Entries = entries.ToArray<object>() };
                }
                case "add":
                {
                    if (string.IsNullOrWhiteSpace(request.Content))
                        return new MemoryStoreResponse { Success = false, Error = "content is required for add." };
                    var id = await _store.AddAsync(request.Category ?? "user", request.Content, cancellationToken).ConfigureAwait(false);
                    return new MemoryStoreResponse { Success = true, Id = id };
                }
                case "delete":
                {
                    var ok = await _store.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
                    return new MemoryStoreResponse { Success = ok, Id = request.Id, Error = ok ? null : $"no entry with id {request.Id}." };
                }
                case "get":
                {
                    var entry = await _store.GetAsync(request.Id, cancellationToken).ConfigureAwait(false);
                    return entry is null
                        ? new MemoryStoreResponse { Success = false, Error = $"no entry with id {request.Id}." }
                        : new MemoryStoreResponse { Success = true, Entry = entry };
                }
                default:
                    return new MemoryStoreResponse { Success = false, Error = $"unknown operation '{op}'." };
            }
        }
    }
}
