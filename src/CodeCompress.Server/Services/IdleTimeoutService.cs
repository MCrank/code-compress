using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeCompress.Server.Services;

internal sealed partial class IdleTimeoutService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly IActivityTracker _activityTracker;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<IdleTimeoutService> _logger;
    private readonly TimeSpan _timeout;

    public IdleTimeoutService(
        IActivityTracker activityTracker,
        IHostApplicationLifetime lifetime,
        IOptions<IdleTimeoutOptions> options,
        ILogger<IdleTimeoutService> logger)
    {
        ArgumentNullException.ThrowIfNull(activityTracker);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _activityTracker = activityTracker;
        _lifetime = lifetime;
        _logger = logger;
        _timeout = TimeSpan.FromSeconds(options.Value.IdleTimeout);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Idle timeout of 0 disables auto-shutdown
        if (_timeout <= TimeSpan.Zero)
        {
            LogIdleTimeoutDisabled(_logger);
            return;
        }

        LogIdleTimeoutStarted(_logger, _timeout.TotalSeconds);

        using var timer = new PeriodicTimer(CheckInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            var elapsed = DateTimeOffset.UtcNow - _activityTracker.LastActivityUtc;

            if (elapsed >= _timeout)
            {
                LogIdleTimeoutReached(_logger, _timeout.TotalSeconds);
                _lifetime.StopApplication();
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idle timeout reached ({TimeoutSeconds}s). Shutting down.")]
    private static partial void LogIdleTimeoutReached(ILogger logger, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idle timeout service started (timeout: {TimeoutSeconds}s).")]
    private static partial void LogIdleTimeoutStarted(ILogger logger, double timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idle timeout disabled (timeout set to 0).")]
    private static partial void LogIdleTimeoutDisabled(ILogger logger);
}
