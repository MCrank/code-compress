using System.Text.Json.Serialization;

namespace CodeCompress.Core.Contracts;

public sealed record ReferenceItemContract
{
    [property: JsonPropertyName("file")]
    public required string File { get; init; }

    [property: JsonPropertyName("line")]
    public int Line { get; init; }

    [property: JsonPropertyName("context_snippet")]
    public required string ContextSnippet { get; init; }

    [property: JsonPropertyName("rank")]
    public int Rank { get; init; }
}

public sealed record FindReferencesResult
{
    [property: JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [property: JsonPropertyName("total_matches")]
    public int? TotalMatches { get; init; }

    [property: JsonPropertyName("results")]
    public IReadOnlyList<ReferenceItemContract>? Results { get; init; }

    [property: JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}
