using CodeCompress.Core.Storage;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class GlobPatternTests
{
    [Test]
    public async Task ParsePlainTextReturnsFts5Strategy()
    {
        var result = GlobPattern.Parse("OrderService");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("OrderService");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParseMultipleWordsReturnsFts5Strategy()
    {
        var result = GlobPattern.Parse("Order Service");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Order Service");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParsePrefixWildcardReturnsPrefixStrategy()
    {
        var result = GlobPattern.Parse("AddMaestro*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Prefix);
        await Assert.That(result.Fts5Query).IsEqualTo("AddMaestro*");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParseSuffixWildcardReturnsSqlLikeStrategy()
    {
        var result = GlobPattern.Parse("*Handler");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.Fts5Query).IsEqualTo(string.Empty);
        await Assert.That(result.SqlLikePattern).IsEqualTo("%Handler");
    }

    [Test]
    public async Task ParseContainsWildcardReturnsSqlLikeStrategy()
    {
        var result = GlobPattern.Parse("*Maestro*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.Fts5Query).IsEqualTo(string.Empty);
        await Assert.That(result.SqlLikePattern).IsEqualTo("%Maestro%");
    }

    [Test]
    public async Task ParseComplexGlobReturnsSqlLikeStrategy()
    {
        var result = GlobPattern.Parse("I*Service");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.Fts5Query).IsEqualTo(string.Empty);
        await Assert.That(result.SqlLikePattern).IsEqualTo("I%Service");
    }

    [Test]
    public async Task IsWildcardOnlyReturnsTrueForSingleStar()
    {
        await Assert.That(GlobPattern.IsWildcardOnly("*")).IsTrue();
    }

    [Test]
    public async Task IsWildcardOnlyReturnsTrueForMultipleStars()
    {
        await Assert.That(GlobPattern.IsWildcardOnly("***")).IsTrue();
    }

    [Test]
    public async Task IsWildcardOnlyReturnsFalseForTextWithStar()
    {
        await Assert.That(GlobPattern.IsWildcardOnly("Add*")).IsFalse();
    }

    [Test]
    public async Task IsWildcardOnlyReturnsFalseForPlainText()
    {
        await Assert.That(GlobPattern.IsWildcardOnly("Order")).IsFalse();
    }

    [Test]
    public async Task ParseEscapesPercentInNonWildcardParts()
    {
        var result = GlobPattern.Parse("*100%Handler");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.SqlLikePattern).IsEqualTo("%100!%Handler");
    }

    [Test]
    public async Task ParseEscapesUnderscoreInNonWildcardParts()
    {
        var result = GlobPattern.Parse("*my_method");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.SqlLikePattern).IsEqualTo("%my!_method");
    }

    [Test]
    public async Task ParseEmptyStringReturnsFts5WithEmptyQuery()
    {
        var result = GlobPattern.Parse("");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ParseWhitespaceOnlyReturnsFts5WithEmptyQuery()
    {
        var result = GlobPattern.Parse("   ");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ParsePrefixWithMultipleTrailingStarsReturnsPrefixStrategy()
    {
        var result = GlobPattern.Parse("Add**");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Prefix);
        await Assert.That(result.Fts5Query).IsEqualTo("Add*");
    }

    [Test]
    public async Task IsWildcardOnlyReturnsTrueForStarsWithWhitespace()
    {
        await Assert.That(GlobPattern.IsWildcardOnly("  *  ")).IsTrue();
    }

    // ── Compound query tests ─────────────────────────────────────────

    [Test]
    public async Task ParseCompoundOrWithPrefixesReturnsFts5Strategy()
    {
        var result = GlobPattern.Parse("Claude* OR Agent*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* OR Agent*");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParseCompoundAndWithPrefixesReturnsFts5Strategy()
    {
        var result = GlobPattern.Parse("Claude* AND Agent*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* AND Agent*");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParseCompoundNotWithPrefixReturnsFts5Strategy()
    {
        var result = GlobPattern.Parse("Claude* NOT Client*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* NOT Client*");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParseCompoundPrefixAndExactReturnsFts5Strategy()
    {
        var result = GlobPattern.Parse("Claude* OR AgentRunner");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* OR AgentRunner");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParseParenthesizedCompoundReturnsFts5Strategy()
    {
        var result = GlobPattern.Parse("(Claude* OR Agent*) AND Service*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("(Claude* OR Agent*) AND Service*");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task ParseMixedPrefixAndSuffixReturnsMixedStrategy()
    {
        var result = GlobPattern.Parse("Claude* OR *Service");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.MixedStrategy);
        await Assert.That(result.ErrorDetail).IsNotNull();
    }

    [Test]
    public async Task ParseMixedPrefixAndContainsReturnsMixedStrategy()
    {
        var result = GlobPattern.Parse("Claude* OR *Claude*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.MixedStrategy);
        await Assert.That(result.ErrorDetail).IsNotNull();
    }

    [Test]
    public async Task ParsePlainTextWithOrStillReturnsFts5()
    {
        var result = GlobPattern.Parse("damage OR health");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("damage OR health");
    }

    [Test]
    public async Task ParseMixedStrategyErrorDetailMentionsIncompatibleTerms()
    {
        var result = GlobPattern.Parse("Claude* OR *Service");

        await Assert.That(result.ErrorDetail!).Contains("*Service");
    }

    [Test]
    public async Task ParseCompoundAllExactTermsReturnsFts5()
    {
        var result = GlobPattern.Parse("Claude OR Agent AND Service");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude OR Agent AND Service");
    }

    [Test]
    public async Task ParseTabSeparatedCompoundReturnsFts5()
    {
        var result = GlobPattern.Parse("Claude*\tOR\tAgent*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* OR Agent*");
    }

    [Test]
    public async Task ParseMixedStrategyErrorDetailSanitizesSpecialChars()
    {
        var result = GlobPattern.Parse("Claude* OR *<script>alert('xss')</script>");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.MixedStrategy);
        await Assert.That(result.ErrorDetail!).DoesNotContain("<script>");
        await Assert.That(result.ErrorDetail!).DoesNotContain("'");
        await Assert.That(result.ErrorDetail!).DoesNotContain("<");
        await Assert.That(result.ErrorDetail!).DoesNotContain(">");
    }

    [Test]
    public async Task ParseMixedStrategyErrorDetailTruncatesLongTerms()
    {
        var longTerm = "*" + new string('a', 200);
        var result = GlobPattern.Parse("Claude* OR " + longTerm);

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.MixedStrategy);
        // ErrorDetail should contain a truncated version, not the full 200-char term
        await Assert.That(result.ErrorDetail!.Length).IsLessThan(300);
    }
}
