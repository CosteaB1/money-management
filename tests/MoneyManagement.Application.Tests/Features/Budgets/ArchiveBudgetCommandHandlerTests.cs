using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Budgets.ArchiveBudget;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Budgets;

public class ArchiveBudgetCommandHandlerTests
{
    private static Budget NewBudget()
    {
        var categoryId = Guid.CreateVersion7();
        return Budget.Create(categoryId, new Money(1_000m, "MDL")).Value;
    }

    [Fact]
    public async Task Handle_WithExistingBudget_Archives()
    {
        Budget budget = NewBudget();
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        var handler = new ArchiveBudgetCommandHandler(db);

        Result result = await handler.Handle(new ArchiveBudgetCommand(budget.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        budget.IsArchived.Should().BeTrue();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithAlreadyArchivedBudget_IsIdempotent()
    {
        Budget budget = NewBudget();
        budget.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        var handler = new ArchiveBudgetCommandHandler(db);

        Result result = await handler.Handle(new ArchiveBudgetCommand(budget.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        budget.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithMissingBudget_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new ArchiveBudgetCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(new ArchiveBudgetCommand(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.NotFound(missingId));
    }
}
