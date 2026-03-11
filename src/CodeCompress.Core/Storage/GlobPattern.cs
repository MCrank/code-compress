using System.Text;

namespace CodeCompress.Core.Storage;

public enum GlobMatchStrategy
{
    Fts5,
    Prefix,
    SqlLike,
}

public sealed class GlobPattern
{
    public GlobMatchStrategy Strategy { get; }
    public string Fts5Query { get; }
    public string? SqlLikePattern { get; }

    private GlobPattern(GlobMatchStrategy strategy, string fts5Query, string? sqlLikePattern)
    {
        Strategy = strategy;
        Fts5Query = fts5Query;
        SqlLikePattern = sqlLikePattern;
    }

    public static GlobPattern CreateFts5(string fts5Query) =>
        new(GlobMatchStrategy.Fts5, fts5Query, null);

    public static GlobPattern CreatePrefix(string fts5Query) =>
        new(GlobMatchStrategy.Prefix, fts5Query, null);

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
}
