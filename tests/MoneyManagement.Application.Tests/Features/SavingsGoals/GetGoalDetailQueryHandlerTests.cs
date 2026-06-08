using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.SavingsGoals;
using MoneyManagement.Application.Features.SavingsGoals.GetGoalDetail;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.SavingsGoals;

public class GetGoalDetailQueryHandlerTests
{
    private static IDateTimeProvider Clock(DateTime utcNow)
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        return clock;
    }

    private static Account NewAccount(string currency = "MDL", decimal opening = 0m)
    {
        Result<Account> result = Account.Create(
            "Linked",
            AccountType.BankDeposit,
            new Money(opening, currency),
            new DateOnly(2026, 1, 1),
            null);
        return result.Value;
    }

    private static Transaction NewTransaction(
        Guid accountId,
        TransactionDirection direction,
        decimal amount,
        string currency,
        DateOnly date,
        string description = "test")
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date,
            direction,
            new Money(amount, currency),
            description,
            TransactionSource.Manual);
        return result.Value;
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetGoalDetailQueryHandler(
            db, FakeFxConverter.Identity(), Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));

        var missingId = Guid.CreateVersion7();
        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotFound(missingId));
    }

    [Fact]
    public async Task Handle_ArchivedGoal_StillReturnsDetail()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal goal = SavingsGoal.Create("Closed", new Money(10_000m, "MDL"), null, null, clock).Value;
        goal.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsArchived.Should().BeTrue();
        result.Value.Id.Should().Be(goal.Id);
    }

    [Fact]
    public async Task Handle_ManualMode_ContributionsReflectTableRowsSortedDesc()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create("Manual", new Money(10_000m, "MDL"), null, null, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(3_000m, "MDL"));

        SavingsGoalContribution older = SavingsGoalContribution.Create(
            goal.Id, new Money(1_000m, "MDL"), new DateOnly(2026, 2, 10), null, clock).Value;
        SavingsGoalContribution middle = SavingsGoalContribution.Create(
            goal.Id, new Money(1_500m, "MDL"), new DateOnly(2026, 3, 20), "March", clock).Value;
        SavingsGoalContribution newer = SavingsGoalContribution.Create(
            goal.Id, new Money(500m, "MDL"), new DateOnly(2026, 4, 5), null, clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            savingsGoals: [goal],
            savingsGoalContributions: [older, middle, newer]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        GoalDetailDto dto = result.Value;
        dto.IsLinkedMode.Should().BeFalse();
        dto.Contributions.Should().HaveCount(3);
        dto.Contributions[0].OccurredOn.Should().Be(new DateOnly(2026, 4, 5));
        dto.Contributions[1].OccurredOn.Should().Be(new DateOnly(2026, 3, 20));
        dto.Contributions[2].OccurredOn.Should().Be(new DateOnly(2026, 2, 10));
        dto.Contributions.Should().OnlyContain(c => c.Source == GoalContributionSource.Manual);
        dto.Contributions[1].Notes.Should().Be("March");
    }

    [Fact]
    public async Task Handle_ManualMode_SavedHistory_WalksRunningTotalsCorrectly()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create("Manual", new Money(10_000m, "MDL"), null, null, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(3_000m, "MDL"));

        SavingsGoalContribution jan = SavingsGoalContribution.Create(
            goal.Id, new Money(1_000m, "MDL"), new DateOnly(2026, 1, 15), null, clock).Value;
        SavingsGoalContribution feb = SavingsGoalContribution.Create(
            goal.Id, new Money(1_500m, "MDL"), new DateOnly(2026, 2, 10), null, clock).Value;
        SavingsGoalContribution apr = SavingsGoalContribution.Create(
            goal.Id, new Money(500m, "MDL"), new DateOnly(2026, 4, 5), null, clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            savingsGoals: [goal],
            savingsGoalContributions: [jan, feb, apr]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        IReadOnlyList<GoalSavedPointDto> history = result.Value.SavedHistory;
        history.Should().NotBeEmpty();
        // Final point closes on the read-clock's "today" with the current saved value.
        history[^1].AsOf.Should().Be(new DateOnly(2026, 5, 15));
        history[^1].Saved.Should().Be(3_000m);
        // Running totals are monotonic non-decreasing for positive-only contributions.
        for (int i = 1; i < history.Count; i++)
        {
            history[i].Saved.Should().BeGreaterThanOrEqualTo(history[i - 1].Saved);
        }
        // Sanity-check intermediate values: by end-of-Feb running = 1000 + 1500 = 2500.
        history.Should().Contain(p => p.AsOf == new DateOnly(2026, 2, 28) && p.Saved == 2_500m);
    }

    [Fact]
    public async Task Handle_ManualMode_NoContributions_RendersTwoPoints()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal goal = SavingsGoal.Create("Fresh", new Money(1_000m, "MDL"), null, null, clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.Value.SavedHistory.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Handle_ManualMode_CreatedTodayWithContributionToday_SavedHistoryHasDistinctAsOf()
    {
        // Repro for the duplicate-AsOf bug: a manual goal created *today* that
        // gets a contribution dated *today*. The created-on baseline must NOT
        // be prepended when it would collide with today's point.
        var todayUtc = new DateTime(2026, 5, 29, 0, 0, 0, DateTimeKind.Utc);
        IDateTimeProvider clock = Clock(todayUtc);

        SavingsGoal goal = SavingsGoal.Create("Fresh", new Money(10_000m, "MDL"), null, null, clock).Value;
        goal.CreatedAt = todayUtc;
        goal.SetManualSaved(new Money(500m, "MDL"));

        SavingsGoalContribution today = SavingsGoalContribution.Create(
            goal.Id, new Money(500m, "MDL"), new DateOnly(2026, 5, 29), null, clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            savingsGoals: [goal],
            savingsGoalContributions: [today]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        IReadOnlyList<GoalSavedPointDto> history = result.Value.SavedHistory;
        history.Select(p => p.AsOf).Should().OnlyHaveUniqueItems();
        // Collapses to the single correct point: today carries the saved value.
        history[^1].AsOf.Should().Be(new DateOnly(2026, 5, 29));
        history[^1].Saved.Should().Be(500m);
    }

    [Fact]
    public async Task Handle_ManualMode_CreatedTodayNoContributions_SavedHistoryHasDistinctAsOf()
    {
        var todayUtc = new DateTime(2026, 5, 29, 0, 0, 0, DateTimeKind.Utc);
        IDateTimeProvider clock = Clock(todayUtc);

        SavingsGoal goal = SavingsGoal.Create("Fresh", new Money(1_000m, "MDL"), null, null, clock).Value;
        goal.CreatedAt = todayUtc;

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        IReadOnlyList<GoalSavedPointDto> history = result.Value.SavedHistory;
        history.Select(p => p.AsOf).Should().OnlyHaveUniqueItems();
        history[^1].AsOf.Should().Be(new DateOnly(2026, 5, 29));
    }

    [Fact]
    public async Task Handle_LinkedMode_CreatedToday_SavedHistoryHasDistinctAsOf()
    {
        var todayUtc = new DateTime(2026, 5, 29, 0, 0, 0, DateTimeKind.Utc);
        IDateTimeProvider clock = Clock(todayUtc);

        Account account = NewAccount("MDL", opening: 0m);
        Transaction inflow = NewTransaction(
            account.Id, TransactionDirection.Income, 750m, "MDL", new DateOnly(2026, 5, 29), "deposit");

        SavingsGoal goal = SavingsGoal.Create(
            "Linked", new Money(10_000m, "MDL"), null, account.Id, clock).Value;
        goal.CreatedAt = todayUtc;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [inflow],
            savingsGoals: [goal]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        IReadOnlyList<GoalSavedPointDto> history = result.Value.SavedHistory;
        history.Select(p => p.AsOf).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Handle_LinkedMode_ContributionsDerivedFromTransactionsSigned()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Account account = NewAccount("MDL", opening: 0m);
        Transaction inflow = NewTransaction(
            account.Id, TransactionDirection.Income, 800m, "MDL", new DateOnly(2026, 3, 1), "salary");
        Transaction outflow = NewTransaction(
            account.Id, TransactionDirection.Expense, 200m, "MDL", new DateOnly(2026, 3, 10), "fee");

        SavingsGoal goal = SavingsGoal.Create(
            "Linked", new Money(10_000m, "MDL"), null, account.Id, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [inflow, outflow],
            savingsGoals: [goal]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        GoalDetailDto dto = result.Value;
        dto.IsLinkedMode.Should().BeTrue();
        dto.LinkedAccountId.Should().Be(account.Id);
        dto.Contributions.Should().HaveCount(2);
        dto.Contributions.Should().OnlyContain(c => c.Source == GoalContributionSource.LinkedAccountTransaction);
        dto.Contributions.Should().OnlyContain(c => c.Id == null);
        // Sort desc: 2026-03-10 first.
        dto.Contributions[0].Amount.Should().Be(-200m);
        dto.Contributions[1].Amount.Should().Be(800m);
        dto.Saved.Should().Be(600m);
    }

    [Fact]
    public async Task Handle_LinkedMode_MissingFxRate_FlagsAndOmitsRow()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Account account = NewAccount("CHF", opening: 0m);
        Transaction tx = NewTransaction(
            account.Id, TransactionDirection.Income, 100m, "CHF", new DateOnly(2026, 3, 1));

        SavingsGoal goal = SavingsGoal.Create(
            "Linked", new Money(10_000m, "MDL"), null, account.Id, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [tx],
            savingsGoals: [goal]);

        // Identity converter returns null for non-identity pairs, simulating
        // no usable rate.
        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.Value.MissingFxRate.Should().BeTrue();
        result.Value.Contributions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AvgMonthly_NullWhenLessThan30DaysOfHistory()
    {
        IDateTimeProvider readClock = Clock(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create("New", new Money(10_000m, "MDL"), null, null, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(200m, "MDL"));

        SavingsGoalContribution contrib = SavingsGoalContribution.Create(
            goal.Id, new Money(200m, "MDL"), new DateOnly(2026, 1, 5), null, readClock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            savingsGoals: [goal],
            savingsGoalContributions: [contrib]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.Value.Pace.AvgMonthlyContribution.Should().BeNull();
        result.Value.Pace.ProjectedCompletionDate.Should().BeNull();
        result.Value.Pace.MonthsToAchieveAtPace.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ProjectedCompletion_NullWhenAvgIsZeroOrNegative()
    {
        // Created Jan 1, read June 1 (5 months in). Single withdrawal makes
        // the trailing 90-day average negative -> no forward projection.
        IDateTimeProvider readClock = Clock(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create("Sliding", new Money(10_000m, "MDL"), null, null, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(1_000m, "MDL"));

        // A withdrawal inside the 90-day window pulls avg below zero.
        SavingsGoalContribution withdrawal = SavingsGoalContribution.Create(
            goal.Id, new Money(-500m, "MDL"), new DateOnly(2026, 5, 1), null, readClock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            savingsGoals: [goal],
            savingsGoalContributions: [withdrawal]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.Value.Pace.AvgMonthlyContribution.Should().NotBeNull();
        result.Value.Pace.AvgMonthlyContribution!.Value.Should().BeLessThan(0m);
        result.Value.Pace.ProjectedCompletionDate.Should().BeNull();
        result.Value.Pace.MonthsToAchieveAtPace.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Achieved_ProjectedNullAndMonthsZero()
    {
        IDateTimeProvider readClock = Clock(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create("Done", new Money(1_000m, "MDL"), null, null, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(1_200m, "MDL"));

        SavingsGoalContribution contrib = SavingsGoalContribution.Create(
            goal.Id, new Money(1_200m, "MDL"), new DateOnly(2026, 4, 1), null, readClock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            savingsGoals: [goal],
            savingsGoalContributions: [contrib]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.Value.Status.Should().Be(GoalStatus.Achieved);
        result.Value.Pace.ProjectedCompletionDate.Should().BeNull();
        result.Value.Pace.MonthsToAchieveAtPace.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_AbsurdlySlowPace_ClampsAtFiftyYears()
    {
        // 100 MDL/month average against a 1,000,000 MDL gap -> ~833 years.
        // Clamp to 600 months (50 years) so the projected date doesn't overflow.
        IDateTimeProvider readClock = Clock(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create(
            "Ambitious", new Money(1_000_000m, "MDL"), null, null, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(100m, "MDL"));

        // Trickle contribution inside the 90-day window: ~3.3 MDL/month avg.
        SavingsGoalContribution trickle = SavingsGoalContribution.Create(
            goal.Id, new Money(10m, "MDL"), new DateOnly(2026, 5, 1), null, readClock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            savingsGoals: [goal],
            savingsGoalContributions: [trickle]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.Value.Pace.MonthsToAchieveAtPace.Should().Be(600m);
        result.Value.Pace.ProjectedCompletionDate.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_LinkedMode_AvgMonthly_ComputedFromBalanceAtWindowStart()
    {
        // Goal created Jan 1, read June 1 -> the 90-day pace window starts ~Mar 3.
        // The linked account has income+expense rows BEFORE the window start, so
        // the balance-at-window-start computation walks both the income and the
        // expense accumulation branches, and there is >1 month of window so a
        // trailing average is produced (not the short-history null).
        IDateTimeProvider readClock = Clock(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Account account = NewAccount("MDL", opening: 1_000m);
        Transaction earlyIncome = NewTransaction(
            account.Id, TransactionDirection.Income, 2_000m, "MDL", new DateOnly(2026, 1, 15), "salary");
        Transaction earlyExpense = NewTransaction(
            account.Id, TransactionDirection.Expense, 500m, "MDL", new DateOnly(2026, 1, 20), "rent");
        Transaction recentIncome = NewTransaction(
            account.Id, TransactionDirection.Income, 1_000m, "MDL", new DateOnly(2026, 5, 1), "bonus");

        SavingsGoal goal = SavingsGoal.Create(
            "Linked pace", new Money(50_000m, "MDL"), null, account.Id, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [earlyIncome, earlyExpense, recentIncome],
            savingsGoals: [goal]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsLinkedMode.Should().BeTrue();
        // Balance today = 1000 + 2000 - 500 + 1000 = 3500; balance at window start
        // (only pre-window rows) = 1000 + 2000 - 500 = 2500; the +1000 bonus lands
        // inside the window, so the trailing average is strictly positive.
        result.Value.Saved.Should().Be(3_500m);
        result.Value.Pace.AvgMonthlyContribution.Should().NotBeNull();
        result.Value.Pace.AvgMonthlyContribution!.Value.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task Handle_LinkedMode_ShortHistory_AvgMonthlyIsNull()
    {
        // Linked goal created 10 days before the read clock -> the pace window is
        // under one month, so the linked avg-monthly computation returns null
        // (the monthsInWindow < 1 branch).
        IDateTimeProvider readClock = Clock(new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Account account = NewAccount("MDL", opening: 500m);

        SavingsGoal goal = SavingsGoal.Create(
            "Young linked", new Money(10_000m, "MDL"), null, account.Id, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            savingsGoals: [goal]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsLinkedMode.Should().BeTrue();
        result.Value.Pace.AvgMonthlyContribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LinkedMode_DanglingAccount_RendersEmptyHistory()
    {
        // Goal links an account id absent from the DB (FK normally prevents this).
        // The defensive dangling branch must produce a zero-saved, empty-history
        // detail without throwing.
        IDateTimeProvider readClock = Clock(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var danglingAccountId = Guid.CreateVersion7();

        SavingsGoal goal = SavingsGoal.Create(
            "Dangling", new Money(10_000m, "MDL"), null, danglingAccountId, createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);

        var handler = new GetGoalDetailQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<GoalDetailDto> result = await handler.Handle(
            new GetGoalDetailQuery(goal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsLinkedMode.Should().BeTrue();
        result.Value.Saved.Should().Be(0m);
        result.Value.Contributions.Should().BeEmpty();
        result.Value.SavedHistory.Should().NotBeEmpty();
        result.Value.Pace.AvgMonthlyContribution.Should().BeNull();
    }
}
