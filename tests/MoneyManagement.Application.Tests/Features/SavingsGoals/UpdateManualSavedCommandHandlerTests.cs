using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.SavingsGoals.UpdateManualSaved;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.SavingsGoals;

public class UpdateManualSavedCommandHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static SavingsGoal NewManualGoal() =>
        SavingsGoal.Create("Goal", new Money(10_000m, "MDL"), null, null, Clock()).Value;

    private static SavingsGoal NewLinkedGoal()
    {
        var linkedId = Guid.CreateVersion7();
        return SavingsGoal.Create("Goal", new Money(10_000m, "MDL"), null, linkedId, Clock()).Value;
    }

    [Fact]
    public async Task Handle_HappyPath_UpdatesAndSaves()
    {
        SavingsGoal goal = NewManualGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        Result result = await handler.Handle(
            new UpdateManualSavedCommand(goal.Id, 2_500m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(2_500m);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InLinkedMode_Rejects()
    {
        SavingsGoal goal = NewLinkedGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        Result result = await handler.Handle(
            new UpdateManualSavedCommand(goal.Id, 2_500m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotInManualMode);
    }

    [Fact]
    public async Task Handle_InLinkedMode_WritesNoContributionRow()
    {
        SavingsGoal goal = NewLinkedGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        await handler.Handle(
            new UpdateManualSavedCommand(goal.Id, 2_500m), CancellationToken.None);

        List<SavingsGoalContribution> rows = await EfQuery(db.SavingsGoalContributions);
        rows.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NegativeAmount_Rejects()
    {
        SavingsGoal goal = NewManualGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        Result result = await handler.Handle(
            new UpdateManualSavedCommand(goal.Id, -1m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.ManualSavedMustBeNonNegative);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(
            new UpdateManualSavedCommand(missingId, 100m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotFound(missingId));
    }

    [Fact]
    public async Task Handle_PositiveDelta_WritesContributionRow()
    {
        SavingsGoal goal = NewManualGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        Result result = await handler.Handle(
            new UpdateManualSavedCommand(goal.Id, 2_500m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        List<SavingsGoalContribution> rows = await EfQuery(db.SavingsGoalContributions);
        rows.Should().HaveCount(1);
        rows[0].GoalId.Should().Be(goal.Id);
        rows[0].Amount.Amount.Should().Be(2_500m);
        rows[0].Amount.Currency.Should().Be("MDL");
        rows[0].OccurredOn.Should().Be(Today);
        rows[0].Notes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NegativeDelta_WritesNegativeContributionRow()
    {
        SavingsGoal goal = NewManualGoal();
        goal.SetManualSaved(new Money(5_000m, "MDL"));
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        Result result = await handler.Handle(
            new UpdateManualSavedCommand(goal.Id, 2_000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        List<SavingsGoalContribution> rows = await EfQuery(db.SavingsGoalContributions);
        rows.Should().HaveCount(1);
        rows[0].Amount.Amount.Should().Be(-3_000m);
    }

    [Fact]
    public async Task Handle_ZeroDelta_WritesNoContributionRowButStillSucceeds()
    {
        SavingsGoal goal = NewManualGoal();
        goal.SetManualSaved(new Money(1_000m, "MDL"));
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new UpdateManualSavedCommandHandler(db, Clock());

        Result result = await handler.Handle(
            new UpdateManualSavedCommand(goal.Id, 1_000m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        List<SavingsGoalContribution> rows = await EfQuery(db.SavingsGoalContributions);
        rows.Should().BeEmpty();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // The FakeDbSet's IQueryable is fine for projection, but Add stores rows in
    // a List that we read back synchronously. ToListAsync on the queryable
    // returns the same backing list.
    private static Task<List<T>> EfQuery<T>(Microsoft.EntityFrameworkCore.DbSet<T> set)
        where T : class
        => Task.FromResult(set.AsEnumerable().ToList());
}
