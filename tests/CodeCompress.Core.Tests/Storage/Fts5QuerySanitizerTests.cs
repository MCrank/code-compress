using CodeCompress.Core.Storage;

namespace CodeCompress.Core.Tests.Storage;

internal sealed class Fts5QuerySanitizerTests
{
    [Test]
    public async Task SanitizeSimpleWordPassesThrough()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage");

        await Assert.That(result).IsEqualTo("damage");
    }

    [Test]
    public async Task SanitizeMultipleWordsPassesThrough()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage health");

        await Assert.That(result).IsEqualTo("damage health");
    }

    [Test]
    public async Task SanitizeBooleanOperatorsAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage OR health");

        await Assert.That(result).IsEqualTo("damage OR health");
    }

    [Test]
    public async Task SanitizeQuotedPhraseAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("\"damage result\"");

        await Assert.That(result).IsEqualTo("\"damage result\"");
    }

    [Test]
    public async Task SanitizePrefixMatchAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("combat*");

        await Assert.That(result).IsEqualTo("combat*");
    }

    [Test]
    public async Task SanitizeUnbalancedQuotesStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage \"result");

        await Assert.That(result).IsEqualTo("damage result");
    }

    [Test]
    public async Task SanitizeColumnFilterStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("name:foo");

        await Assert.That(result).IsEqualTo("foo");
    }

    [Test]
    public async Task SanitizeNearOperatorStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("NEAR(damage, health)");

        await Assert.That(result).IsEqualTo("damage  health");
    }

    [Test]
    public async Task SanitizeCaretOperatorStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("^damage");

        await Assert.That(result).IsEqualTo("damage");
    }

    [Test]
    public async Task SanitizeUnbalancedParenthesesStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("(damage OR");

        await Assert.That(result).IsEqualTo("damage OR");
    }

    [Test]
    public async Task SanitizeBalancedParenthesesAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("(damage OR health)");

        await Assert.That(result).IsEqualTo("(damage OR health)");
    }

    [Test]
    public async Task SanitizeEmptyAfterSanitizationFallsBackToLiteral()
    {
        var result = Fts5QuerySanitizer.Sanitize("^");

        await Assert.That(result).IsEqualTo("\"^\"");
    }

    [Test]
    public async Task SanitizeEmptyInputReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.Sanitize("");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeWhitespaceInputReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.Sanitize("   ");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeGlobSimplePatternPassesThrough()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("*.luau");

        await Assert.That(result).IsEqualTo("*.luau");
    }

    [Test]
    public async Task SanitizeGlobPathPatternPassesThrough()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("src/services/*.lua");

        await Assert.That(result).IsEqualTo("src/services/*.lua");
    }

    [Test]
    public async Task SanitizeGlobMaliciousInputSanitized()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("*.luau; DROP TABLE files");

        await Assert.That(result).IsEqualTo("*.luauDROPTABLEfiles");
    }

    [Test]
    public async Task SanitizeGlobEmptyInputReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.SanitizeGlob("");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeExcessClosingParenthesesStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage OR health))");

        await Assert.That(result).IsEqualTo("damage OR health");
    }

    [Test]
    public async Task SanitizeNearWithDistanceStripped()
    {
        var result = Fts5QuerySanitizer.Sanitize("NEAR/3(damage, health)");

        await Assert.That(result).IsEqualTo("damage  health");
    }

    [Test]
    public async Task SanitizeNotOperatorAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage NOT health");

        await Assert.That(result).IsEqualTo("damage NOT health");
    }

    [Test]
    public async Task SanitizeAndOperatorAllowed()
    {
        var result = Fts5QuerySanitizer.Sanitize("damage AND health");

        await Assert.That(result).IsEqualTo("damage AND health");
    }

    [Test]
    public async Task SanitizeAsGlobPlainTextReturnsFts5Strategy()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("OrderService");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("OrderService");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task SanitizeAsGlobPrefixReturnsPrefixStrategy()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("AddMaestro*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Prefix);
        await Assert.That(result.Fts5Query).IsEqualTo("AddMaestro*");
        await Assert.That(result.SqlLikePattern).IsNull();
    }

    [Test]
    public async Task SanitizeAsGlobSuffixReturnsSqlLikeStrategy()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("*Handler");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.SqlLikePattern).IsEqualTo("%Handler");
    }

    [Test]
    public async Task SanitizeAsGlobContainsReturnsSqlLikeStrategy()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("*Maestro*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.SqlLikePattern).IsEqualTo("%Maestro%");
    }

    [Test]
    public async Task SanitizeAsGlobComplexPatternReturnsSqlLikeStrategy()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("I*Service");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.SqlLike);
        await Assert.That(result.SqlLikePattern).IsEqualTo("I%Service");
    }

    [Test]
    public async Task SanitizeAsGlobEmptyReturnsEmptyFts5()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeAsGlobWildcardOnlyReturnsEmptyFts5()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SanitizeAsGlobPrefixWithColumnFilterSanitized()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("name:Add*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Prefix);
        await Assert.That(result.Fts5Query).IsEqualTo("Add*");
    }

    // ── Compound query sanitization tests ────────────────────────────

    [Test]
    public async Task SanitizeAsGlobCompoundOrPrefixReturnsFts5()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("Claude* OR Agent*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* OR Agent*");
    }

    [Test]
    public async Task SanitizeAsGlobCompoundPrefixAndExactReturnsFts5()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("Claude* OR AgentRunner");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* OR AgentRunner");
    }

    [Test]
    public async Task SanitizeAsGlobCompoundWithColumnFilterSanitized()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("name:Claude* OR Agent*");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.Fts5);
        await Assert.That(result.Fts5Query).IsEqualTo("Claude* OR Agent*");
    }

    [Test]
    public async Task SanitizeAsGlobMixedStrategyPassesThrough()
    {
        var result = Fts5QuerySanitizer.SanitizeAsGlob("Claude* OR *Service");

        await Assert.That(result.Strategy).IsEqualTo(GlobMatchStrategy.MixedStrategy);
        await Assert.That(result.ErrorDetail).IsNotNull();
    }

    // ── TokenizeForSearch tests ────────────────────────────────────────

    [Test]
    public async Task TokenizeForSearchMultiWordReturnsOrJoinedTerms()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("PromptInjectionGuard sanitize detect injection");

        await Assert.That(result).IsEqualTo("PromptInjectionGuard OR sanitize OR detect OR injection");
    }

    [Test]
    public async Task TokenizeForSearchStripsStopwords()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("the authentication handler for tenants");

        await Assert.That(result).IsEqualTo("authentication OR handler OR tenants");
    }

    [Test]
    public async Task TokenizeForSearchSingleWordReturnsSameWord()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("PathValidator");

        await Assert.That(result).IsEqualTo("PathValidator");
    }

    [Test]
    public async Task TokenizeForSearchAllStopwordsReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("the a an in of");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TokenizeForSearchEmptyReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TokenizeForSearchWhitespaceReturnsEmpty()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("   ");

        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TokenizeForSearchPreservesExistingFts5Operators()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("damage OR health");

        await Assert.That(result).IsEqualTo("damage OR health");
    }

    [Test]
    public async Task TokenizeForSearchPreservesWildcardPatterns()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("*Guard*");

        await Assert.That(result).IsEqualTo("*Guard*");
    }

    [Test]
    public async Task TokenizeForSearchSanitizesEachToken()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("name:foo bar");

        await Assert.That(result).IsEqualTo("foo OR bar");
    }

    [Test]
    public async Task TokenizeForSearchStopwordsCaseInsensitive()
    {
        var result = Fts5QuerySanitizer.TokenizeForSearch("The Authentication FOR Tenants");

        await Assert.That(result).IsEqualTo("Authentication OR Tenants");
    }
}
