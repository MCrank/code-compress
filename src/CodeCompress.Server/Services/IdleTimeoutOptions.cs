namespace CodeCompress.Server.Services;

internal sealed class IdleTimeoutOptions
{
    /// <summary>
    /// Idle timeout in seconds. 0 disables auto-shutdown. Default is 600 (10 minutes).
    /// </summary>
    public int IdleTimeout { get; set; } = 600;
}
