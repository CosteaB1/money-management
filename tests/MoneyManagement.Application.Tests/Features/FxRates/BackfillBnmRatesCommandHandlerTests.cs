using FluentAssertions;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.FxRates.BackfillBnmRates;
using MoneyManagement.Application.Features.FxRates.RefreshBnmRates;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.FxRates;

public class BackfillBnmRatesCommandHandlerTests
{
    private static readonly DateOnly Today = new(2026, 5, 24);
    private static readonly DateTime FixedNow = new(2026, 5, 24, 9, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> RefreshHandler(
        RefreshBnmRatesResponse response)
    {
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> handler =
            Substitute.For<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();
        handler
            .Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));
        return handler;
    }

    [Fact]
    public async Task Handle_InclusiveRange_CallsRefreshPerDateAndAggregates()
    {
        var perDate = new RefreshBnmRatesResponse(Fetched: 3, Inserted: 1, Updated: 0, Skipped: 2);
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refresh = RefreshHandler(perDate);
        var handler = new BackfillBnmRatesCommandHandler(refresh, Clock());

        var command = new BackfillBnmRatesCommand(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5));
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DaysProcessed.Should().Be(5);
        result.Value.Fetched.Should().Be(15);
        result.Value.Inserted.Should().Be(5);
        result.Value.Updated.Should().Be(0);
        result.Value.Skipped.Should().Be(10);

        await refresh.Received(5).Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());

        // Every date in the inclusive range was dispatched exactly once.
        for (var d = new DateOnly(2026, 5, 1); d <= new DateOnly(2026, 5, 5); d = d.AddDays(1))
        {
            DateOnly expected = d;
            await refresh.Received(1).Handle(
                Arg.Is<RefreshBnmRatesCommand>(c => c.Date == expected && c.CurrencyFilter == null),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Handle_NullTo_DefaultsToToday()
    {
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refresh =
            RefreshHandler(new RefreshBnmRatesResponse(0, 0, 0, 0));
        var handler = new BackfillBnmRatesCommandHandler(refresh, Clock());

        var command = new BackfillBnmRatesCommand(Today.AddDays(-2), To: null);
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DaysProcessed.Should().Be(3);
        await refresh.Received(3).Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FutureTo_IsClampedToToday()
    {
        // command.To in the future is clamped down to today (the `to > today`
        // branch). From == today, so exactly one day is processed.
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refresh =
            RefreshHandler(new RefreshBnmRatesResponse(0, 0, 0, 0));
        var handler = new BackfillBnmRatesCommandHandler(refresh, Clock());

        var command = new BackfillBnmRatesCommand(Today, To: Today.AddDays(10));
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DaysProcessed.Should().Be(1);
        await refresh.Received(1).Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FutureStart_FailsAndNeverRefreshes()
    {
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refresh =
            RefreshHandler(new RefreshBnmRatesResponse(0, 0, 0, 0));
        var handler = new BackfillBnmRatesCommandHandler(refresh, Clock());

        var command = new BackfillBnmRatesCommand(Today.AddDays(1));
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("fx.backfill_future_start");
        await refresh.DidNotReceive().Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ToBeforeFrom_FailsInvalidRange()
    {
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refresh =
            RefreshHandler(new RefreshBnmRatesResponse(0, 0, 0, 0));
        var handler = new BackfillBnmRatesCommandHandler(refresh, Clock());

        var command = new BackfillBnmRatesCommand(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 5));
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("fx.backfill_invalid_range");
        await refresh.DidNotReceive().Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RangeTooLarge_FailsRangeTooLarge()
    {
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refresh =
            RefreshHandler(new RefreshBnmRatesResponse(0, 0, 0, 0));
        var handler = new BackfillBnmRatesCommandHandler(refresh, Clock());

        // 801 days apart, both in the past — only the size guard should trip.
        DateOnly from = Today.AddDays(-810);
        var command = new BackfillBnmRatesCommand(from, from.AddDays(801));
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("fx.backfill_range_too_large");
        await refresh.DidNotReceive().Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PerDateFailure_ShortCircuitsAndPropagatesError()
    {
        ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refresh =
            Substitute.For<ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse>>();
        var expectedError = Error.Failure("fx.refresh_failed", "boom");
        refresh
            .Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new RefreshBnmRatesResponse(1, 1, 0, 0)),
                Result.Failure<RefreshBnmRatesResponse>(expectedError));

        var handler = new BackfillBnmRatesCommandHandler(refresh, Clock());

        var command = new BackfillBnmRatesCommand(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 5));
        Result<BackfillBnmRatesResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(expectedError);

        // Stopped after the failing (second) call — days 3..5 never dispatched.
        await refresh.Received(2).Handle(Arg.Any<RefreshBnmRatesCommand>(), Arg.Any<CancellationToken>());
    }
}
