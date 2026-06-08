using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.SavingsGoals.UpdateGoal;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.SavingsGoals;

public class UpdateGoalCommandHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FutureDate = new(2026, 12, 31);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static Account NewAccount(string name = "XTB")
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Brokerage,
            new Money(0m, "MDL"),
            new DateOnly(2026, 1, 1),
            null);
        return result.Value;
    }

    private static SavingsGoal NewGoal(Guid? linkedAccountId = null) =>
        SavingsGoal.Create("Goal", new Money(10_000m, "MDL"), FutureDate, linkedAccountId, Clock()).Value;

    [Fact]
    public async Task Handle_UpdatesAllFields_OnManualGoal()
    {
        SavingsGoal goal = NewGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateGoalCommandHandler(db, Clock());

        var command = new UpdateGoalCommand(
            goal.Id,
            "Renamed",
            25_000m,
            new DateOnly(2027, 6, 30),
            LinkedAccountId: null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.Name.Should().Be("Renamed");
        goal.TargetAmount.Amount.Should().Be(25_000m);
        goal.TargetDate.Should().Be(new DateOnly(2027, 6, 30));
        goal.LinkedAccountId.Should().BeNull();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SwitchesManualToLinked()
    {
        SavingsGoal goal = NewGoal(); // manual mode
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            savingsGoals: [goal]);

        var handler = new UpdateGoalCommandHandler(db, Clock());

        var command = new UpdateGoalCommand(
            goal.Id,
            goal.Name,
            goal.TargetAmount.Amount,
            goal.TargetDate,
            LinkedAccountId: account.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().Be(account.Id);
        goal.ManualSavedAmount.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SwitchesLinkedToManual_ResetsManualSavedToZero()
    {
        Account account = NewAccount();
        SavingsGoal goal = NewGoal(linkedAccountId: account.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            savingsGoals: [goal]);

        var handler = new UpdateGoalCommandHandler(db, Clock());

        var command = new UpdateGoalCommand(
            goal.Id,
            goal.Name,
            goal.TargetAmount.Amount,
            goal.TargetDate,
            LinkedAccountId: null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().BeNull();
        goal.ManualSavedAmount.Should().NotBeNull();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_EditingAlreadyManualGoal_PreservesManualSavedAmount()
    {
        // B-2 regression: editing a goal that is ALREADY in manual mode (target
        // only, still manual) must NOT wipe the user's saved progress to zero.
        SavingsGoal goal = NewGoal(); // manual mode, starts at 0
        Result seed = goal.SetManualSaved(new Money(2_500m, "MDL"));
        seed.IsSuccess.Should().BeTrue();

        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateGoalCommandHandler(db, Clock());

        var command = new UpdateGoalCommand(
            goal.Id,
            "Renamed",
            42_000m, // changed target
            FutureDate,
            LinkedAccountId: null); // still manual

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().BeNull();
        goal.TargetAmount.Amount.Should().Be(42_000m);
        goal.ManualSavedAmount.Should().NotBeNull();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(2_500m);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RelinkingSameAccount_IsNoOp_StaysLinked()
    {
        // Re-saving an already-linked goal pointed at the same account must not
        // re-run LinkAccount (a no-op) and must keep the linked mode.
        Account account = NewAccount();
        SavingsGoal goal = NewGoal(linkedAccountId: account.Id);
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [account],
            savingsGoals: [goal]);

        var handler = new UpdateGoalCommandHandler(db, Clock());

        var command = new UpdateGoalCommand(
            goal.Id,
            "Renamed",
            goal.TargetAmount.Amount,
            goal.TargetDate,
            LinkedAccountId: account.Id);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().Be(account.Id);
        goal.ManualSavedAmount.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LinkedAccountMissing_ReturnsAccountNotFound()
    {
        SavingsGoal goal = NewGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateGoalCommandHandler(db, Clock());

        var missingId = Guid.CreateVersion7();
        var command = new UpdateGoalCommand(
            goal.Id,
            goal.Name,
            goal.TargetAmount.Amount,
            goal.TargetDate,
            LinkedAccountId: missingId);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingId));
    }

    [Fact]
    public async Task Handle_BlankName_ReturnsRenameFailure_AndDoesNotSave()
    {
        SavingsGoal goal = NewGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateGoalCommandHandler(db, Clock());

        var command = new UpdateGoalCommand(goal.Id, "   ", 25_000m, FutureDate, LinkedAccountId: null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NameRequired);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonPositiveTarget_ReturnsUpdateTargetFailure()
    {
        SavingsGoal goal = NewGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateGoalCommandHandler(db, Clock());

        var command = new UpdateGoalCommand(goal.Id, "Renamed", 0m, FutureDate, LinkedAccountId: null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.TargetMustBePositive);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PastTargetDate_ReturnsUpdateDateFailure()
    {
        SavingsGoal goal = NewGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateGoalCommandHandler(db, Clock());

        DateOnly pastDate = DateOnly.FromDateTime(FixedNow).AddDays(-1);
        var command = new UpdateGoalCommand(goal.Id, "Renamed", 25_000m, pastDate, LinkedAccountId: null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.TargetDateInPast);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new UpdateGoalCommandHandler(db, Clock());

        var missingId = Guid.CreateVersion7();
        var command = new UpdateGoalCommand(missingId, "Goal", 1_000m, null, null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotFound(missingId));
    }
}
