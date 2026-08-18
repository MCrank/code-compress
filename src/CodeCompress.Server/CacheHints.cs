using ModelContextProtocol.Protocol;

namespace CodeCompress.Server;

/// <summary>
/// Applies SEP-2549 cache hints to the compile-time-static tools/prompts listings, and sorts
/// them so a client observes a byte-identical order across independent server processes.
/// </summary>
/// <remarks>
/// A client that keys its cache by serverInfo.version may serve a stale listing for up to
/// <see cref="ListingTtl"/> after a rebuild that isn't a tagged release, since
/// AssemblyInformationalVersion only changes on a VersionPrefix bump. This affects displayed
/// tool/prompt metadata only — tool calls always execute against the live process.
/// </remarks>
internal static class CacheHints
{
    internal static readonly TimeSpan ListingTtl = TimeSpan.FromHours(1);
    internal const CacheScope ListingScope = CacheScope.Private;

    public static ListToolsResult ApplyToToolsResult(ListToolsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        result.Tools = [.. result.Tools.OrderBy(t => t.Name, StringComparer.Ordinal)];
        result.TimeToLive = ListingTtl;
        result.CacheScope = ListingScope;

        return result;
    }

    public static ListPromptsResult ApplyToPromptsResult(ListPromptsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        result.Prompts = [.. result.Prompts.OrderBy(p => p.Name, StringComparer.Ordinal)];
        result.TimeToLive = ListingTtl;
        result.CacheScope = ListingScope;

        return result;
    }
}
