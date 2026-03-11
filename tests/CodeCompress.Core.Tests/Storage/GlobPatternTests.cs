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
}
