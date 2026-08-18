using System.Text.Json.Serialization;

namespace CodeCompress.Core.Contracts;

public sealed record RepoEntryContract
{
    [property: JsonPropertyName("project_root")]
    public required string ProjectRoot { get; init; }

    [property: JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    [property: JsonPropertyName("file_count")]
    public int FileCount { get; init; }

    [property: JsonPropertyName("symbol_count")]
    public int SymbolCount { get; init; }

    [property: JsonPropertyName("last_indexed")]
    public DateTimeOffset LastIndexed { get; init; }

    [property: JsonPropertyName("status")]
    public required string Status { get; init; }
}

public sealed record ListReposResult
{
    [property: JsonPropertyName("repos")]
    public required IReadOnlyList<RepoEntryContract> Repos { get; init; }
}
