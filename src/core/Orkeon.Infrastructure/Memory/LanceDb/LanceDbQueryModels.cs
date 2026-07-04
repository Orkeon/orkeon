using System.Text.Json.Serialization;

namespace Orkeon.Infrastructure.Memory.LanceDb;

/// <summary>
/// JSON body of <c>POST /v1/table/{name}/query</c> — the <c>QueryTableRequest</c> of
/// the Lance REST Namespace spec. <c>vector</c> and <c>k</c> are required by the spec;
/// <c>vector</c> is nullable and must be <see langword="null"/> for full-text or
/// filter-only queries, hence it is always serialized.
/// </summary>
internal sealed class LanceDbQueryRequest
{
    [JsonPropertyName("vector")]
    public LanceDbQueryVector? Vector { get; set; }

    [JsonPropertyName("k")]
    public int K { get; set; }

    [JsonPropertyName("filter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Filter { get; set; }

    [JsonPropertyName("prefilter")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Prefilter { get; set; }

    [JsonPropertyName("distance_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DistanceType { get; set; }

    [JsonPropertyName("vector_column")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VectorColumn { get; set; }

    [JsonPropertyName("offset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Offset { get; set; }

    [JsonPropertyName("columns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LanceDbQueryColumns? Columns { get; set; }

    [JsonPropertyName("full_text_query")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LanceDbFullTextQuery? FullTextQuery { get; set; }
}

/// <summary>Query vector wrapper (<c>single_vector</c> form) of the Lance REST Namespace spec.</summary>
internal sealed class LanceDbQueryVector
{
    [JsonPropertyName("single_vector")]
    public IReadOnlyList<float>? SingleVector { get; set; }
}

/// <summary>Column projection (<c>column_names</c> form) of the Lance REST Namespace spec.</summary>
internal sealed class LanceDbQueryColumns
{
    [JsonPropertyName("column_names")]
    public IReadOnlyList<string>? ColumnNames { get; set; }
}

/// <summary>Full-text query wrapper (<c>string_query</c> form) of the Lance REST Namespace spec.</summary>
internal sealed class LanceDbFullTextQuery
{
    [JsonPropertyName("string_query")]
    public LanceDbStringFtsQuery? StringQuery { get; set; }
}

/// <summary>String full-text query (<c>StringFtsQuery</c>) of the Lance REST Namespace spec.</summary>
internal sealed class LanceDbStringFtsQuery
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("columns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Columns { get; set; }
}

/// <summary>JSON error envelope (<c>ErrorResponse</c>) returned by the Lance REST Namespace server.</summary>
internal sealed class LanceDbErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

/// <summary>
/// One decoded row of a query response: the stored record plus the server-side
/// ranking columns when present (<c>_distance</c> for vector search,
/// <c>_score</c> for full-text search).
/// </summary>
internal sealed record LanceDbQueryRow(LanceDbRecord Record, float? Distance, float? Score);
