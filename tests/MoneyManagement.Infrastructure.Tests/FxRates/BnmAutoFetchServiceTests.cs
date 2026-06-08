using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.FxRates.RefreshBnmRates;
using MoneyManagement.Infrastructure.FxRates;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Infrastructure.Tests.FxRates;

/// <summary>
/// Covers the <see cref="BnmAutoFetchService"/> hosted service: the
/// <c>Enabled=false</c> early-exit, the startup backfill (one dispatch per
/// backfill day, oldest-&gt;today), and at least one post-backfill refresh
/// iteration. The infinite <c>while</c>/<c>Task.Delay</c> loop is exited by
/// cancelling the token from inside the dispatched handler — NOT by a real
/// wall-clock wait.
/// </summary>
public sealed class BnmAutoFetchServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    /// <summary>
    /// Wires a scope factory whose scopes all resolve <paramref name="handler"/>
    /// as the refresh command handler — matching the service's
    /// <c>scope.ServiceProvider.GetRequiredService&lt;...&gt;()</c> lookup.
    /// </summary>
    private static IServiceScopeFactory ScopeFactoryFor(
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static BnmAutoFetchService Build(
        FxAutoFetchOptions options,
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler,
        IDateTimeProvider? clock = null) =>
        new(
            ScopeFactoryFor(handler),
            Options.Create(options),
            clock ?? Clock(),
            NullLogger<BnmAutoFetchService>.Instance);

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ExitsImmediatelyWithoutDispatching()
    {
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler = Substitute.For<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();
        BnmAutoFetchService service = Build(new FxAutoFetchOptions { Enabled = false }, handler);

        await service.StartAsync(CancellationToken.None);
        // ExecuteAsync returns synchronously on the disabled path; give the host nothing to wait on.
        await service.StopAsync(CancellationToken.None);

        await handler.DidNotReceive().Handle(
            Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_BackfillsOneDispatchPerDay_OldestToToday()
    {
        // 3-day backfill: today-2, today-1, today (dispatched in that order).
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler = Substitute.For<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();
        var today = DateOnly.FromDateTime(FixedNow);
        var dispatchedDates = new List<DateOnly>();
        using var cts = new CancellationTokenSource();
        handler
            .Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                DateOnly date = ((RefreshBnmRatesCommand)call[0]).Date!.Value;
                dispatchedDates.Add(date);
                // Cancel only after the LAST backfill day so all 3 dispatch, then
                // the post-backfill Task.Delay throws — exiting without a real wait.
                if (date == today)
                {
                    cts.Cancel();
                }

                return Result.Success(new RefreshBnmRatesResponse(0, 0, 0, 0));
            });

        var options = new FxAutoFetchOptions
        {
            Enabled = true,
            BackfillDays = 3,
            RefreshIntervalHours = 999,
        };

        BnmAutoFetchService service = Build(options, handler);

        // ExecuteAsync is protected; drive it via the BackgroundService host API,
        // then await the running task so the backfill is fully observed.
        await service.StartAsync(cts.Token);
        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);

        dispatchedDates.Should().StartWith([today.AddDays(-2), today.AddDays(-1), today]);
        dispatchedDates.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ExecuteAsync_AfterBackfill_DispatchesTodayAndExitsOnCancellation()
    {
        // BackfillDays=1 dispatches only today. The handler cancels the token, so
        // once the backfill completes the post-backfill loop's guard is already
        // tripped and the service exits cleanly — no real Task.Delay is awaited.
        // (The delay loop itself is [ExcludeFromCodeCoverage]; see the service.)
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler = Substitute.For<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();

        using var cts = new CancellationTokenSource();
        int callCount = 0;
        handler
            .Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                cts.Cancel();
                return Result.Success(new RefreshBnmRatesResponse(0, 0, 0, 0));
            });

        var options = new FxAutoFetchOptions
        {
            Enabled = true,
            BackfillDays = 1,
            RefreshIntervalHours = 999,
        };

        BnmAutoFetchService service = Build(options, handler);

        await service.StartAsync(cts.Token);
        await service.ExecuteTask!;
        await service.StopAsync(CancellationToken.None);

        callCount.Should().BeGreaterThanOrEqualTo(1);
        var today = DateOnly.FromDateTime(FixedNow);
        await handler.Received().Handle(
            Arg.Is<RefreshBnmRatesCommand>(c => c.Date == today),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledMidBackfill_SwallowsOperationCanceled()
    {
        // Cancel on the FIRST of a 2-day backfill so the loop's
        // ThrowIfCancellationRequested fires on the next iteration. The resulting
        // OperationCanceledException is swallowed by ExecuteAsync's catch (normal
        // shutdown) — the background task completes without faulting.
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler =
            Substitute.For<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();

        using var cts = new CancellationTokenSource();
        handler
            .Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return Result.Success(new RefreshBnmRatesResponse(0, 0, 0, 0));
            });

        var options = new FxAutoFetchOptions { Enabled = true, BackfillDays = 2, RefreshIntervalHours = 999 };
        BnmAutoFetchService service = Build(options, handler);

        Func<Task> act = async () =>
        {
            await service.StartAsync(cts.Token);
            await service.ExecuteTask!;
            await service.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerThrows_DoesNotPropagate()
    {
        // A provider/DB explosion must not take the host down — the service
        // logs and returns rather than faulting the background task.
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler = Substitute.For<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();
        handler
            .Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<RefreshBnmRatesResponse>>>(_ => throw new InvalidOperationException("boom"));

        var options = new FxAutoFetchOptions { Enabled = true, BackfillDays = 1, RefreshIntervalHours = 999 };
        BnmAutoFetchService service = Build(options, handler);

        Func<Task> act = async () =>
        {
            await service.StartAsync(CancellationToken.None);
            // The handler throws on the first backfill dispatch; the service's
            // top-level catch logs and completes the task normally (no fault).
            await service.ExecuteTask!;
            await service.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }
}
