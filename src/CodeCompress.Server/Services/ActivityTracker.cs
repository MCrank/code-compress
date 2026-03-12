namespace CodeCompress.Server.Services;

internal sealed class ActivityTracker : IActivityTracker
{
    private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;

    public DateTimeOffset LastActivityUtc => _lastActivityUtc;

    public void RecordActivity()
    {
        _lastActivityUtc = DateTimeOffset.UtcNow;
    }
}
