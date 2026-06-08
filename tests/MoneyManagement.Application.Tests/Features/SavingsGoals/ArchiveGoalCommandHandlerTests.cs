using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.SavingsGoals.ArchiveGoal;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.SavingsGoals;

public class ArchiveGoalCommandHandlerTests
{
    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static SavingsGoal NewGoal() =>
        SavingsGoal.Create("Goal", new Money(10_000m, "MDL"), null, null, Clock()).Value;

    [Fact]
    public async Task Handle_Archives_AndSaves()
    {
        SavingsGoal goal = NewGoal();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new ArchiveGoalCommandHandler(db);

        Result result = await handler.Handle(new ArchiveGoalCommand(goal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.IsArchived.Should().BeTrue();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyArchived_IsIdempotent()
    {
        SavingsGoal goal = NewGoal();
        goal.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(savingsGoals: [goal]);
        var handler = new ArchiveGoalCommandHandler(db);

        Result result = await handler.Handle(new ArchiveGoalCommand(goal.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        goal.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new ArchiveGoalCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(new ArchiveGoalCommand(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotFound(missingId));
    }
}
