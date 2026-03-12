using CodeCompress.Server.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeCompress.Server.Tests.Services;

internal sealed class IdleTimeoutServiceTests
{
    [Test]
    public async Task ZeroTimeoutDisablesAutoShutdown()
    {
        var activityTracker = Substitute.For<IActivityTracker>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var options = Options.Create(new IdleTimeoutOptions { IdleTimeout = 0 });
        var logger = NullLogger<IdleTimeoutService>.Instance;

        using var service = new IdleTimeoutService(activityTracker, lifetime, options, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // ExecuteAsync should return immediately when timeout is 0
        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(100, cts.Token).ConfigureAwait(false);

        lifetime.DidNotReceive().StopApplication();

        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task ShutdownAfterIdleTimeout()
    {
        var activityTracker = Substitute.For<IActivityTracker>();
        // Set last activity to far in the past so timeout is immediately exceeded
        activityTracker.LastActivityUtc.Returns(DateTimeOffset.UtcNow.AddMinutes(-30));

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var options = Options.Create(new IdleTimeoutOptions { IdleTimeout = 1 });
        var logger = NullLogger<IdleTimeoutService>.Instance;

        using var service = new IdleTimeoutService(activityTracker, lifetime, options, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cts.Token).ConfigureAwait(false);

        // Wait for the periodic timer to fire (check interval is 5s)
        await Task.Delay(TimeSpan.FromSeconds(6), cts.Token).ConfigureAwait(false);

        lifetime.Received(1).StopApplication();

        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task RecentActivityPreventsShutdown()
    {
        var activityTracker = Substitute.For<IActivityTracker>();
        // Activity just happened — timeout of 600s should not trigger
        activityTracker.LastActivityUtc.Returns(DateTimeOffset.UtcNow);

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var options = Options.Create(new IdleTimeoutOptions { IdleTimeout = 600 });
        var logger = NullLogger<IdleTimeoutService>.Instance;

        using var service = new IdleTimeoutService(activityTracker, lifetime, options, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        await service.StartAsync(cts.Token).ConfigureAwait(false);

        // Wait for one check cycle
        await Task.Delay(TimeSpan.FromSeconds(6), cts.Token).ConfigureAwait(false);

        lifetime.DidNotReceive().StopApplication();

        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task NegativeTimeoutDisablesAutoShutdown()
    {
        var activityTracker = Substitute.For<IActivityTracker>();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var options = Options.Create(new IdleTimeoutOptions { IdleTimeout = -1 });
        var logger = NullLogger<IdleTimeoutService>.Instance;

        using var service = new IdleTimeoutService(activityTracker, lifetime, options, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.StartAsync(cts.Token).ConfigureAwait(false);
        await Task.Delay(100, cts.Token).ConfigureAwait(false);

        lifetime.DidNotReceive().StopApplication();

        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task DefaultTimeoutIsTenMinutes()
    {
        var options = new IdleTimeoutOptions();

        await Assert.That(options.IdleTimeout).IsEqualTo(600);
    }
}
