using System.Text;
using System.Text.RegularExpressions;

namespace CodeCompress.Core.Storage;

public enum GlobMatchStrategy
{
    Fts5,
    Prefix,
    SqlLike,
    MixedStrategy,
}

public sealed partial class GlobPattern
{
    private static readonly string[] Fts5Operators = ["OR", "AND", "NOT"];

    public GlobMatchStrategy Strategy { get; }
    public string Fts5Query { get; }
    public string? SqlLikePattern { get; }
    public string? ErrorDetail { get; }

    private GlobPattern(GlobMatchStrategy strategy, string fts5Query, string? sqlLikePattern, string? errorDetail = null)
    {
        Strategy = strategy;
        Fts5Query = fts5Query;
        SqlLikePattern = sqlLikePattern;
        ErrorDetail = errorDetail;
    }

    public static GlobPattern CreateFts5(string fts5Query) =>
        new(GlobMatchStrategy.Fts5, fts5Query, null);

    public static GlobPattern CreatePrefix(string fts5Query) =>
        new(GlobMatchStrategy.Prefix, fts5Query, null);

    public static GlobPattern CreateSqlLike(string fts5Query, string sqlLikePattern) =>
        new(GlobMatchStrategy.SqlLike, fts5Query, sqlLikePattern);

    /// <summary>
    /// Returns true if the query is a plain term with no wildcards, FTS5 operators, or quotes.
    /// Plain terms are candidates for automatic contains-match fallback.
    /// </summary>
    public static bool IsPlainTerm(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmed = query.Trim();

        if (trimmed.Contains('*', StringComparison.Ordinal) || trimmed.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Contains('"', StringComparison.Ordinal))
        {
            return false;
        }

        if (ContainsFts5Operators(trimmed))
        {
            return false;
        }

        return true;
    }

    public static bool IsWildcardOnly(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        return query.Trim().All(c => c == '*');
    }

    public static GlobPattern Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new GlobPattern(GlobMatchStrategy.Fts5, string.Empty, null);
        }

        var trimmed = query.Trim();

        if (!trimmed.Contains('*', StringComparison.Ordinal))
        {
            return new GlobPattern(GlobMatchStrategy.Fts5, trimmed, null);
        }

        if (IsWildcardOnly(trimmed))
        {
            return new GlobPattern(GlobMatchStrategy.Fts5, string.Empty, null);
        }

        // Normalize whitespace (tabs, multiple spaces) before compound query detection
        var normalized = WhitespacePattern().Replace(trimmed, " ");

        // Check for compound queries with FTS5 operators before single-term analysis
        if (ContainsFts5Operators(normalized))
        {
            return ParseCompoundQuery(normalized);
        }

        // Check if this is a pure prefix pattern: text followed by one or more trailing *
        // with no * elsewhere
        var withoutTrailingStars = trimmed.TrimEnd('*');
        if (withoutTrailingStars.Length > 0 && !withoutTrailingStars.Contains('*', StringComparison.Ordinal))
        {
            return new GlobPattern(GlobMatchStrategy.Prefix, withoutTrailingStars + "*", null);
        }

        // All other patterns: suffix, contains, complex → SQL LIKE
        var likePattern = ConvertToSqlLike(trimmed);
        return new GlobPattern(GlobMatchStrategy.SqlLike, string.Empty, likePattern);
    }

    private static bool ContainsFts5Operators(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => Fts5Operators.Contains(t, StringComparer.Ordinal));
    }

    private static GlobPattern ParseCompoundQuery(string query)
    {
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var incompatibleTerms = new List<string>();

        foreach (var token in tokens)
        {
            if (Fts5Operators.Contains(token, StringComparer.Ordinal))
            {
                continue;
            }

            if (!IsFts5CompatibleTerm(token))
            {
                incompatibleTerms.Add(SanitizeTermForDisplay(token));
            }
        }

        if (incompatibleTerms.Count == 0)
        {
            // All terms are FTS5-compatible (plain text or prefix*) — FTS5 handles natively
            return new GlobPattern(GlobMatchStrategy.Fts5, query, null);
        }

        // Mixed strategies — cannot combine FTS5 and SQL LIKE in one query
        var errorDetail = $"Cannot combine prefix (term*) and suffix/contains (*term) patterns in one query. " +
                          $"Incompatible terms: {string.Join(", ", incompatibleTerms)}. " +
                          $"Run separate queries for each pattern type.";
        return new GlobPattern(GlobMatchStrategy.MixedStrategy, query, null, errorDetail);
    }

    private static bool IsFts5CompatibleTerm(string term)
    {
        // Strip parentheses — they're FTS5 grouping, not part of the term
        var cleaned = term.Trim('(', ')');

        if (string.IsNullOrEmpty(cleaned))
        {
            return true;
        }

        if (!cleaned.Contains('*', StringComparison.Ordinal))
        {
            return true; // plain text
        }

        // Check if it's a prefix pattern: text followed by trailing *
        var withoutTrailingStars = cleaned.TrimEnd('*');
        return withoutTrailingStars.Length > 0 && !withoutTrailingStars.Contains('*', StringComparison.Ordinal);
    }

    /// <summary>
    /// Sanitizes a term for inclusion in error messages returned to AI agents.
    /// Allows only alphanumeric, *, _, ., - to prevent prompt injection via error output.
    /// </summary>
    private static string SanitizeTermForDisplay(string term)
    {
        var cleaned = term.Trim('(', ')');
        var sanitized = string.Concat(cleaned.Where(c => char.IsLetterOrDigit(c) || c is '*' or '_' or '.' or '-'));
        return sanitized.Length > 64 ? sanitized[..64] : sanitized;
    }

    private static string ConvertToSqlLike(string globPattern)
    {
        var sb = new StringBuilder(globPattern.Length + 4);
        var i = 0;

        while (i < globPattern.Length)
        {
            var c = globPattern[i];
            if (c == '*')
            {
                // Collapse consecutive stars into single %
                sb.Append('%');
                while (i < globPattern.Length && globPattern[i] == '*')
                {
                    i++;
                }
            }
            else
            {
                // Escape SQL LIKE special characters in non-wildcard parts (using '!' as ESCAPE char)
                if (c is '%' or '_')
                {
                    sb.Append('!');
                }

                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
