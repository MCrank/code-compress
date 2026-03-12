namespace CodeCompress.Server.Services;

internal interface IActivityTracker
{
    internal DateTimeOffset LastActivityUtc { get; }

    internal void RecordActivity();
}
