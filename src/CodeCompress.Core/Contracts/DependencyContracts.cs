using System.Text.Json.Serialization;

namespace CodeCompress.Core.Contracts;

public sealed record BlastRadiusDepthContract
{
    [property: JsonPropertyName("depth")]
    public int Depth { get; init; }

    [property: JsonPropertyName("files")]
    public required IReadOnlyList<string> Files { get; init; }
}

public sealed record BlastRadiusToolResult
{
    [property: JsonPropertyName("total_affected")]
    public int? TotalAffected { get; init; }

    [property: JsonPropertyName("depths")]
    public IReadOnlyList<BlastRadiusDepthContract>? Depths { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}

public sealed record UnusedSymbolContract
{
    [property: JsonPropertyName("name")]
    public required string Name { get; init; }

    [property: JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [property: JsonPropertyName("signature")]
    public required string Signature { get; init; }
}

public sealed record FindUnusedSymbolsResult
{
    [property: JsonPropertyName("results")]
    public IReadOnlyList<UnusedSymbolContract>? Results { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}
