using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.FxRates.RefreshBnmRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.FxRates;

/// <summary>
/// Background service that keeps the FxRate table populated from BNM.
/// On startup: backfills the last <c>BackfillDays</c> days (today included),
/// one date at a time so each day's counts land in the log. After that:
/// re-fetches today every <c>RefreshIntervalHours</c>. Manual rates always
/// win — the underlying handler skips them on collision.
/// </summary>
internal sealed class BnmAutoFetchService(
    IServiceScopeFactory scopeFactory,
    IOptions<FxAutoFetchOptions> options,
    IDateTimeProvider clock,
    ILogger<BnmAutoFetchService> logger) : BackgroundService
{
    private readonly FxAutoFetchOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("BNM auto-fetch disabled via Fx:AutoFetch:Enabled = false.");
            return;
        }

        try
        {
            await BackfillAsync(stoppingToken);
            await RunRefreshLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — fall through silently.
        }
        catch (Exception ex)
        {
            // Don't take the host down because BNM blew up; the loop is best-effort.
            logger.LogError(ex, "BNM auto-fetch service crashed; will not retry until next restart.");
        }
    }

    // Excluded from coverage: the only exit from this loop is a real
    // Task.Delay(interval) elapsing or the token cancelling mid-delay. The
    // interval is clamped to >= 1h and there's no injectable timer, so it can't
    // be driven in a unit test without an actual wall-clock wait. The backfill
    // path (which shares DispatchAsync) is fully covered.
    [ExcludeFromCodeCoverage(Justification = "Infinite Task.Delay loop; only reachable via a real >=1h wall-clock wait.")]
    private async Task RunRefreshLoopAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.RefreshIntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stoppingToken);
            await RefreshTodayAsync(stoppingToken);
        }
    }

    private async Task BackfillAsync(CancellationToken cancellationToken)
    {
        int days = Math.Max(1, _options.BackfillDays);
        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Walk oldest -> today so the most recent day's log line is the final one.
        for (int offset = days - 1; offset >= 0; offset--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateOnly target = today.AddDays(-offset);
            await DispatchAsync(target, cancellationToken);
        }
    }

    // Only invoked from the coverage-excluded refresh loop (post-backfill);
    // DispatchAsync itself is exercised by the backfill tests.
    [ExcludeFromCodeCoverage(Justification = "Only called from the delay-gated refresh loop.")]
    private Task RefreshTodayAsync(CancellationToken cancellationToken) =>
        DispatchAsync(DateOnly.FromDateTime(clock.UtcNow), cancellationToken);

    private async Task DispatchAsync(DateOnly date, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();

        await handler.Handle(
            new RefreshBnmRatesCommand(Date: date, CurrencyFilter: null),
            cancellationToken);
    }
}
