using System.Text.Json.Serialization;

namespace CodeCompress.Core.Contracts;

public sealed record SymbolChildContract
{
    [property: JsonPropertyName("name")]
    public required string Name { get; init; }

    [property: JsonPropertyName("signature")]
    public required string Signature { get; init; }

    [property: JsonPropertyName("expand_with")]
    public required string ExpandWith { get; init; }
}

public sealed record ModuleSymbolContract
{
    [property: JsonPropertyName("name")]
    public required string Name { get; init; }

    [property: JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [property: JsonPropertyName("parent")]
    public string? Parent { get; init; }

    [property: JsonPropertyName("signature")]
    public required string Signature { get; init; }

    [property: JsonPropertyName("line")]
    public int Line { get; init; }

    [property: JsonPropertyName("doc_comment")]
    public string? DocComment { get; init; }
}

public sealed record ModuleDependencyContract
{
    [property: JsonPropertyName("requires_path")]
    public required string RequiresPath { get; init; }

    [property: JsonPropertyName("alias")]
    public string? Alias { get; init; }
}

public sealed record GetModuleApiResult
{
    [property: JsonPropertyName("module")]
    public string? Module { get; init; }

    [property: JsonPropertyName("symbols")]
    public IReadOnlyList<ModuleSymbolContract>? Symbols { get; init; }

    [property: JsonPropertyName("dependencies")]
    public IReadOnlyList<ModuleDependencyContract>? Dependencies { get; init; }

    [property: JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}

public sealed record GetSymbolResult
{
    [property: JsonPropertyName("name")]
    public string? Name { get; init; }

    [property: JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [property: JsonPropertyName("parent")]
    public string? Parent { get; init; }

    [property: JsonPropertyName("file")]
    public string? File { get; init; }

    [property: JsonPropertyName("line_start")]
    public int? LineStart { get; init; }

    [property: JsonPropertyName("line_end")]
    public int? LineEnd { get; init; }

    [property: JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [property: JsonPropertyName("source_code")]
    public string? SourceCode { get; init; }

    [property: JsonPropertyName("truncated")]
    public bool? Truncated { get; init; }

    [property: JsonPropertyName("source_size_bytes")]
    public int? SourceSizeBytes { get; init; }

    [property: JsonPropertyName("children")]
    public IReadOnlyList<SymbolChildContract>? Children { get; init; }

    [property: JsonPropertyName("guidance")]
    public string? Guidance { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }

    [property: JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [property: JsonPropertyName("candidates")]
    public IReadOnlyList<string>? Candidates { get; init; }
}

public sealed record ExpandSymbolResult
{
    [property: JsonPropertyName("name")]
    public string? Name { get; init; }

    [property: JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [property: JsonPropertyName("parent")]
    public string? Parent { get; init; }

    [property: JsonPropertyName("file")]
    public string? File { get; init; }

    [property: JsonPropertyName("line_start")]
    public int? LineStart { get; init; }

    [property: JsonPropertyName("line_end")]
    public int? LineEnd { get; init; }

    [property: JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [property: JsonPropertyName("doc_comment")]
    public string? DocComment { get; init; }

    [property: JsonPropertyName("source_code")]
    public string? SourceCode { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }

    [property: JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [property: JsonPropertyName("candidates")]
    public IReadOnlyList<string>? Candidates { get; init; }

    [property: JsonPropertyName("guidance")]
    public string? Guidance { get; init; }
}

public sealed record SymbolItemContract
{
    [property: JsonPropertyName("name")]
    public string? Name { get; init; }

    [property: JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [property: JsonPropertyName("parent")]
    public string? Parent { get; init; }

    [property: JsonPropertyName("file")]
    public string? File { get; init; }

    [property: JsonPropertyName("line_start")]
    public int? LineStart { get; init; }

    [property: JsonPropertyName("line_end")]
    public int? LineEnd { get; init; }

    [property: JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [property: JsonPropertyName("source_code")]
    public string? SourceCode { get; init; }

    [property: JsonPropertyName("truncated")]
    public bool? Truncated { get; init; }

    [property: JsonPropertyName("source_size_bytes")]
    public int? SourceSizeBytes { get; init; }

    [property: JsonPropertyName("children")]
    public IReadOnlyList<SymbolChildContract>? Children { get; init; }

    [property: JsonPropertyName("guidance")]
    public string? Guidance { get; init; }
}

public sealed record SymbolErrorContract
{
    [property: JsonPropertyName("symbol")]
    public required string Symbol { get; init; }

    [property: JsonPropertyName("error")]
    public required string Error { get; init; }

    [property: JsonPropertyName("code")]
    public required string Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool Retryable { get; init; }

    [property: JsonPropertyName("guidance")]
    public string? Guidance { get; init; }
}

public sealed record GetSymbolsResult
{
    [property: JsonPropertyName("results")]
    public IReadOnlyList<SymbolItemContract>? Results { get; init; }

    [property: JsonPropertyName("errors")]
    public IReadOnlyList<SymbolErrorContract>? Errors { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}

public sealed record SymbolSearchItemContract
{
    [property: JsonPropertyName("name")]
    public required string Name { get; init; }

    [property: JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [property: JsonPropertyName("parent")]
    public string? Parent { get; init; }

    [property: JsonPropertyName("file")]
    public required string File { get; init; }

    [property: JsonPropertyName("line")]
    public int Line { get; init; }

    [property: JsonPropertyName("signature")]
    public required string Signature { get; init; }

    [property: JsonPropertyName("snippet")]
    public string? Snippet { get; init; }

    [property: JsonPropertyName("rank")]
    public int Rank { get; init; }
}

public sealed record SearchSymbolsResult
{
    [property: JsonPropertyName("query")]
    public string? Query { get; init; }

    [property: JsonPropertyName("total_matches")]
    public int? TotalMatches { get; init; }

    [property: JsonPropertyName("fallback_used")]
    public bool? FallbackUsed { get; init; }

    [property: JsonPropertyName("results")]
    public IReadOnlyList<SymbolSearchItemContract>? Results { get; init; }

    [property: JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }

    [property: JsonPropertyName("suggestion")]
    public string? Suggestion { get; init; }

    [property: JsonPropertyName("suggestions")]
    public IReadOnlyList<string>? Suggestions { get; init; }
}

public sealed record TextSearchItemContract
{
    [property: JsonPropertyName("file_path")]
    public required string FilePath { get; init; }

    [property: JsonPropertyName("snippet")]
    public required string Snippet { get; init; }

    [property: JsonPropertyName("rank")]
    public int Rank { get; init; }
}

public sealed record SearchTextResult
{
    [property: JsonPropertyName("query")]
    public string? Query { get; init; }

    [property: JsonPropertyName("total_matches")]
    public int? TotalMatches { get; init; }

    [property: JsonPropertyName("results")]
    public IReadOnlyList<TextSearchItemContract>? Results { get; init; }

    [property: JsonPropertyName("hint")]
    public string? Hint { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }
}

public sealed record HotPathContextLineContract
{
    [property: JsonPropertyName("line_number")]
    public int LineNumber { get; init; }

    [property: JsonPropertyName("text")]
    public required string Text { get; init; }
}

public sealed record HotPathMatchContract
{
    [property: JsonPropertyName("identifier")]
    public required string Identifier { get; init; }

    [property: JsonPropertyName("line")]
    public int Line { get; init; }

    [property: JsonPropertyName("context")]
    public IReadOnlyList<HotPathContextLineContract>? Context { get; init; }
}

public sealed record GetHotPathResult
{
    [property: JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [property: JsonPropertyName("file")]
    public string? File { get; init; }

    [property: JsonPropertyName("total_lines")]
    public int? TotalLines { get; init; }

    [property: JsonPropertyName("returned_lines")]
    public int? ReturnedLines { get; init; }

    [property: JsonPropertyName("matches")]
    public IReadOnlyList<HotPathMatchContract>? Matches { get; init; }

    [property: JsonPropertyName("error")]
    public string? Error { get; init; }

    [property: JsonPropertyName("code")]
    public string? Code { get; init; }

    [property: JsonPropertyName("retryable")]
    public bool? Retryable { get; init; }

    [property: JsonPropertyName("candidates")]
    public IReadOnlyList<string>? Candidates { get; init; }

    [property: JsonPropertyName("guidance")]
    public string? Guidance { get; init; }
}
