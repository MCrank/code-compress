using CodeCompress.Core.Indexing;

namespace CodeCompress.Core.Tests.Indexing;

internal sealed class ChangeTrackerTests
{
    private ChangeTracker _tracker = null!;

    [Before(Test)]
    public void SetUp()
    {
        _tracker = new ChangeTracker();
    }

    [Test]
    public async Task FirstIndexAllNew()
    {
        var current = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2",
            ["c.cs"] = "h3",
        };
        var stored = new Dictionary<string, string>();

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(3);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(0);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(0);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(0);
    }

    [Test]
    public async Task MixOfAllCategories()
    {
        var current = new Dictionary<string, string>
        {
            ["new.cs"] = "h1",
            ["modified.cs"] = "h2_new",
            ["unchanged.cs"] = "h3",
        };
        var stored = new Dictionary<string, string>
        {
            ["modified.cs"] = "h2_old",
            ["unchanged.cs"] = "h3",
            ["deleted.cs"] = "h4",
        };

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(1);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(1);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(1);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(1);
    }

    [Test]
    public async Task NoChanges()
    {
        var files = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2",
            ["c.cs"] = "h3",
        };
        var stored = new Dictionary<string, string>(files);

        var result = _tracker.DetectChanges(files, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(0);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(0);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(0);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(3);
    }

    [Test]
    public async Task AllDeleted()
    {
        var current = new Dictionary<string, string>();
        var stored = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2",
            ["c.cs"] = "h3",
        };

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(0);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(0);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(3);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(0);
    }

    [Test]
    public async Task EmptyProject()
    {
        var current = new Dictionary<string, string>();
        var stored = new Dictionary<string, string>();

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(0);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(0);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(0);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(0);
    }

    [Test]
    public async Task FirstRunStoredEmpty()
    {
        var current = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2",
            ["c.cs"] = "h3",
            ["d.cs"] = "h4",
            ["e.cs"] = "h5",
        };
        var stored = new Dictionary<string, string>();

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(5);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(0);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(0);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SingleModifiedFile()
    {
        var current = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2_changed",
            ["c.cs"] = "h3",
        };
        var stored = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
            ["b.cs"] = "h2",
            ["c.cs"] = "h3",
        };

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(0);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(1);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(0);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(2);
    }

    [Test]
    public async Task HasChangesTrueWhenChangesExist()
    {
        var current = new Dictionary<string, string>
        {
            ["new.cs"] = "h1",
        };
        var stored = new Dictionary<string, string>();

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.HasChanges).IsTrue();
    }

    [Test]
    public async Task HasChangesFalseWhenNoChanges()
    {
        var current = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
        };
        var stored = new Dictionary<string, string>
        {
            ["a.cs"] = "h1",
        };

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.HasChanges).IsFalse();
    }

    [Test]
    public async Task CaseInsensitivePathMatching()
    {
        var current = new Dictionary<string, string>
        {
            ["C:/Project/File.cs"] = "h1",
        };
        var stored = new Dictionary<string, string>
        {
            ["c:/project/file.cs"] = "h1",
        };

        var result = _tracker.DetectChanges(current, stored);

        await Assert.That(result.NewFiles).Count().IsEqualTo(0);
        await Assert.That(result.ModifiedFiles).Count().IsEqualTo(0);
        await Assert.That(result.DeletedFiles).Count().IsEqualTo(0);
        await Assert.That(result.UnchangedFiles).Count().IsEqualTo(1);
    }
}
