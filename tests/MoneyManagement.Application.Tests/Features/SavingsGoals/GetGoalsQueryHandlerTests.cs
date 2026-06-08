using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.SavingsGoals;
using MoneyManagement.Application.Features.SavingsGoals.GetGoals;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.SavingsGoals;

public class GetGoalsQueryHandlerTests
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
        DateOnly? date = null)
    {
        Result<Transaction> result = Transaction.Create(
            accountId,
            date ?? new DateOnly(2026, 3, 1),
            direction,
            new Money(amount, currency),
            "test",
            TransactionSource.Manual);
        return result.Value;
    }

    [Fact]
    public async Task Handle_NoGoals_ReturnsEmpty()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new GetGoalsQueryHandler(
            db,
            FakeFxConverter.Identity(),
            Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ManualMode_UsesGoalsManualSavedAmount()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal goal = SavingsGoal.Create(
            "Manual",
            new Money(10_000m, "MDL"),
            null,
            null,
            clock).Value;
        goal.SetManualSaved(new Money(3_000m, "MDL"));

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        GoalDto dto = result.Value.Single();
        dto.IsLinkedMode.Should().BeFalse();
        dto.Saved.Should().Be(3_000m);
        dto.Remaining.Should().Be(7_000m);
        dto.ProgressPercent.Should().Be(0.30m);
        dto.MissingFxRate.Should().BeFalse();
        dto.LinkedAccountId.Should().BeNull();
        dto.LinkedAccountName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LinkedMode_ComputesBalanceFromTransactions()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Account account = NewAccount("MDL", opening: 1_000m);
        Transaction income = NewTransaction(account.Id, TransactionDirection.Income, 500m, "MDL");
        Transaction expense = NewTransaction(account.Id, TransactionDirection.Expense, 200m, "MDL");

        SavingsGoal goal = SavingsGoal.Create(
            "Linked",
            new Money(10_000m, "MDL"),
            null,
            account.Id,
            clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            transactions: [income, expense],
            savingsGoals: [goal]);

        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        GoalDto dto = result.Value.Single();
        dto.IsLinkedMode.Should().BeTrue();
        dto.LinkedAccountId.Should().Be(account.Id);
        dto.LinkedAccountName.Should().Be(account.Name);
        // 1000 anchor + 500 income - 200 expense = 1300 MDL
        dto.Saved.Should().Be(1_300m);
        dto.Remaining.Should().Be(8_700m);
        dto.MissingFxRate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LinkedMode_DanglingAccountId_DefaultsSavedToZero()
    {
        // Goal links an account id that isn't in the DB (FK normally prevents
        // this, but the defensive zero-saved branch must not blow up).
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var danglingAccountId = Guid.CreateVersion7();

        SavingsGoal goal = SavingsGoal.Create(
            "Dangling",
            new Money(10_000m, "MDL"),
            null,
            danglingAccountId,
            clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);

        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        GoalDto dto = result.Value.Single();
        dto.IsLinkedMode.Should().BeTrue();
        dto.LinkedAccountName.Should().BeNull();
        dto.Saved.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_LinkedMode_NonMdlAccount_ConvertsViaFx()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Account account = NewAccount("USD", opening: 100m);

        SavingsGoal goal = SavingsGoal.Create(
            "Linked",
            new Money(10_000m, "MDL"),
            null,
            account.Id,
            clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            savingsGoals: [goal]);

        IFxConverter fx = FakeFxConverter.WithTable(new Dictionary<string, decimal>
        {
            ["USD"] = 17.50m,
        });

        var handler = new GetGoalsQueryHandler(db, fx, clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        GoalDto dto = result.Value.Single();
        dto.Saved.Should().Be(1_750m);
        dto.MissingFxRate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LinkedMode_NoUsableFxRate_FlagsMissingFxRate()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Account account = NewAccount("CHF", opening: 500m);

        SavingsGoal goal = SavingsGoal.Create(
            "Linked",
            new Money(10_000m, "MDL"),
            null,
            account.Id,
            clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            savingsGoals: [goal]);

        // Identity converter returns null for non-identity pairs.
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        GoalDto dto = result.Value.Single();
        dto.Saved.Should().Be(0m);
        dto.MissingFxRate.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ArchivedGoalsExcluded()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal active = SavingsGoal.Create("Active", new Money(1_000m, "MDL"), null, null, clock).Value;
        SavingsGoal archived = SavingsGoal.Create("Archived", new Money(2_000m, "MDL"), null, null, clock).Value;
        archived.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [active, archived]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        result.Value.Should().HaveCount(1);
        result.Value[0].Name.Should().Be("Active");
    }

    [Fact]
    public async Task Handle_Achieved_OverridesEveryOtherStatus()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal goal = SavingsGoal.Create(
            "Done",
            new Money(1_000m, "MDL"),
            new DateOnly(2026, 4, 30), // would be Behind, but Achieved wins
            null,
            Clock(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc))).Value;
        goal.SetManualSaved(new Money(1_500m, "MDL"));

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        result.Value[0].Status.Should().Be(GoalStatus.Achieved);
        result.Value[0].RequiredMonthlyContribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoTargetDate_AlwaysOnTrack()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal goal = SavingsGoal.Create(
            "Open-ended",
            new Money(10_000m, "MDL"),
            targetDate: null,
            linkedAccountId: null,
            clock).Value;
        // No manual save - 0 / 10000 progress but no deadline.

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        GoalDto dto = result.Value[0];
        dto.Status.Should().Be(GoalStatus.OnTrack);
        dto.RequiredMonthlyContribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PastTargetDate_NotAchieved_IsBehind()
    {
        // Created Jan 1, target Apr 30, today May 15 — past deadline, not done.
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider readClock = Clock(new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create(
            "Late",
            new Money(10_000m, "MDL"),
            new DateOnly(2026, 4, 30),
            null,
            createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(500m, "MDL"));

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        result.Value[0].Status.Should().Be(GoalStatus.Behind);
    }

    [Fact]
    public async Task Handle_OnPaceForDeadline_IsOnTrack()
    {
        // Created 2026-01-01, target 2027-01-01 (~12 months), today
        // 2026-07-01 (~6 months in). Saving 5000 of 10000 puts us right at
        // the linear pace → OnTrack.
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider readClock = Clock(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create(
            "Pacing",
            new Money(10_000m, "MDL"),
            new DateOnly(2027, 1, 1),
            null,
            createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(5_000m, "MDL"));

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        result.Value[0].Status.Should().Be(GoalStatus.OnTrack);
    }

    [Fact]
    public async Task Handle_BehindPaceButBeforeDeadline_IsAtRisk()
    {
        // Created 2026-01-01, target 2027-01-01, today 2026-09-01 (~8mo in).
        // Expected pace at 8/12 of 10000 = ~6667. Saved only 1000 → AtRisk.
        IDateTimeProvider createdClock = Clock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        IDateTimeProvider readClock = Clock(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        SavingsGoal goal = SavingsGoal.Create(
            "Lagging",
            new Money(10_000m, "MDL"),
            new DateOnly(2027, 1, 1),
            null,
            createdClock).Value;
        goal.CreatedAt = createdClock.UtcNow;
        goal.SetManualSaved(new Money(1_000m, "MDL"));

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), readClock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        result.Value[0].Status.Should().Be(GoalStatus.AtRisk);
    }

    [Fact]
    public async Task Handle_RequiredMonthlyContribution_NullWhenNoTargetDate()
    {
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal goal = SavingsGoal.Create(
            "Open-ended",
            new Money(10_000m, "MDL"),
            null,
            null,
            clock).Value;
        goal.SetManualSaved(new Money(2_000m, "MDL"));

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        result.Value[0].RequiredMonthlyContribution.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RequiredMonthlyContribution_ComputedWhenDeadlineSet()
    {
        // Today 2026-05-01, deadline 2026-11-01 (~6 months), target 12000,
        // saved 0 → required = 12000 / 6 = 2000.
        IDateTimeProvider clock = Clock(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        SavingsGoal goal = SavingsGoal.Create(
            "6mo Goal",
            new Money(12_000m, "MDL"),
            new DateOnly(2026, 11, 1),
            null,
            clock).Value;

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new GetGoalsQueryHandler(db, FakeFxConverter.Identity(), clock);

        Result<IReadOnlyList<GoalDto>> result = await handler.Handle(new GetGoalsQuery(), CancellationToken.None);

        decimal? required = result.Value[0].RequiredMonthlyContribution;
        required.Should().NotBeNull();
        // ~6 months between 2026-05-01 and 2026-11-01; ceil ~= 7 (184 days / 30.4375 ≈ 6.045, ceil 7).
        // Required = 12000 / 7 ≈ 1714.29. Loose assertion: positive and ≤ 2000.
        required!.Value.Should().BeGreaterThan(0m).And.BeLessThanOrEqualTo(2_000m);
    }
}
