using System.Text.Json.Serialization;

namespace CodeCompress.Core.Contracts;

public sealed record ParseFailureContract
{
    [property: JsonPropertyName("file_path")]
    public required string FilePath { get; init; }

    [property: JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

public sealed record IndexProjectResult
{
    [property: JsonPropertyName("repo_id")]
    public string? RepoId { get; init; }

    [property: JsonPropertyName("project_root")]
    public string? ProjectRoot { get; init; }

    [property: JsonPropertyName("files_indexed")]
    public int? FilesIndexed { get; init; }

    [property: JsonPropertyName("files_unchanged")]
    public int? FilesUnchanged { get; init; }

    [property: JsonPropertyName("files_errored")]
    public int? FilesErrored { get; init; }

    [property: JsonPropertyName("total_files")]
    public int? TotalFiles { get; init; }

    [property: JsonPropertyName("symbols_found")]
    public int? SymbolsFound { get; init; }

    [property: JsonPropertyName("duration_ms")]
    public long? DurationMs { get; init; }

    [property: JsonPropertyName("parse_errors")]
    public IReadOnlyList<ParseFailureContract>? ParseErrors { get; init; }

    [property: JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}

public sealed record SnapshotCreateResult
{
    [property: JsonPropertyName("snapshot_id")]
    public long? SnapshotId { get; init; }

    [property: JsonPropertyName("label")]
    public string? Label { get; init; }

    [property: JsonPropertyName("file_count")]
    public int? FileCount { get; init; }

    [property: JsonPropertyName("symbol_count")]
    public int? SymbolCount { get; init; }

    [property: JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}

public sealed record InvalidateCacheResult
{
    [property: JsonPropertyName("success")]
    public bool? Success { get; init; }

    [property: JsonPropertyName("message")]
    public string? Message { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}
