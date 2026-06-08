using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.SavingsGoals.CreateGoal;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.SavingsGoals;

public class CreateGoalCommandHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

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

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_ManualMode_PersistsAndReturnsId()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateGoalCommandHandler(db, Clock());

        var command = new CreateGoalCommand(
            "Emergency Fund",
            10_000m,
            new DateOnly(2026, 12, 31),
            LinkedAccountId: null);

        Result<CreateGoalResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SavingsGoal persisted = db.SavingsGoals.Single();
        persisted.Name.Should().Be("Emergency Fund");
        persisted.TargetAmount.Amount.Should().Be(10_000m);
        persisted.LinkedAccountId.Should().BeNull();
        persisted.ManualSavedAmount.Should().NotBeNull();
        persisted.ManualSavedAmount!.Value.Amount.Should().Be(0m);
        result.Value.Id.Should().Be(persisted.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LinkedMode_PersistsWithLink()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CreateGoalCommandHandler(db, Clock());

        var command = new CreateGoalCommand(
            "Snowball",
            50_000m,
            TargetDate: null,
            LinkedAccountId: account.Id);

        Result<CreateGoalResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SavingsGoal persisted = db.SavingsGoals.Single();
        persisted.LinkedAccountId.Should().Be(account.Id);
        persisted.ManualSavedAmount.Should().BeNull();
    }

    [Fact]
    public async Task Handle_LinkedAccountMissing_ReturnsAccountNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateGoalCommandHandler(db, Clock());

        var missingId = Guid.CreateVersion7();
        var command = new CreateGoalCommand(
            "Goal",
            1_000m,
            null,
            LinkedAccountId: missingId);

        Result<CreateGoalResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingId));
        db.SavingsGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_TargetDateInPast_ReturnsValidationError()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateGoalCommandHandler(db, Clock());

        var command = new CreateGoalCommand(
            "Goal",
            1_000m,
            TargetDate: new DateOnly(2026, 4, 30), // FixedNow is 2026-05-01
            LinkedAccountId: null);

        Result<CreateGoalResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.TargetDateInPast);
    }

    [Fact]
    public async Task Handle_BlankName_BubblesDomainError()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateGoalCommandHandler(db, Clock());

        // The FluentValidation decorator catches this in the real pipeline.
        // Here we hit the handler directly to confirm the domain-side
        // safety net also fires.
        var command = new CreateGoalCommand("   ", 1_000m, null, null);

        Result<CreateGoalResponse> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NameRequired);
    }
}
