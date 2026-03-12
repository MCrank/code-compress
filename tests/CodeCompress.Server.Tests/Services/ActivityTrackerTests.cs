using CodeCompress.Server.Services;

namespace CodeCompress.Server.Tests.Services;

internal sealed class ActivityTrackerTests
{
    [Test]
    public async Task InitialLastActivityUtcIsCloseToNow()
    {
        var before = DateTimeOffset.UtcNow;
        var tracker = new ActivityTracker();
        var after = DateTimeOffset.UtcNow;

        await Assert.That(tracker.LastActivityUtc).IsGreaterThanOrEqualTo(before);
        await Assert.That(tracker.LastActivityUtc).IsLessThanOrEqualTo(after);
    }

    [Test]
    public async Task RecordActivityUpdatesLastActivityUtc()
    {
        var tracker = new ActivityTracker();

        var initialTime = tracker.LastActivityUtc;

        // Small delay to ensure time advances
        await Task.Delay(15).ConfigureAwait(false);

        tracker.RecordActivity();

        await Assert.That(tracker.LastActivityUtc).IsGreaterThan(initialTime);
    }

    [Test]
    public async Task MultipleRecordActivityCallsUpdateTimestamp()
    {
        var tracker = new ActivityTracker();

        tracker.RecordActivity();
        var first = tracker.LastActivityUtc;

        await Task.Delay(15).ConfigureAwait(false);

        tracker.RecordActivity();
        var second = tracker.LastActivityUtc;

        await Assert.That(second).IsGreaterThan(first);
    }
}
