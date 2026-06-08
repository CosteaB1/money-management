using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Budgets.UpdateBudgetLimit;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Budgets;

public class UpdateBudgetLimitCommandHandlerTests
{
    private static Budget NewBudget(decimal limit = 1_000m)
    {
        var categoryId = Guid.CreateVersion7();
        return Budget.Create(categoryId, new Money(limit, "MDL")).Value;
    }

    [Fact]
    public async Task Handle_WithValidCommand_UpdatesLimitAndSaves()
    {
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        var handler = new UpdateBudgetLimitCommandHandler(db);

        Result result = await handler.Handle(
            new UpdateBudgetLimitCommand(budget.Id, 2_750m), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        budget.MonthlyLimit.Amount.Should().Be(2_750m);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNonPositiveLimit_ReturnsDomainFailure_AndDoesNotSave()
    {
        // The FluentValidation validator shadows this in the pipeline, but the
        // handler's own domain guard (Budget.UpdateLimit) must still reject it.
        Budget budget = NewBudget(1_000m);
        IApplicationDbContext db = FakeApplicationDbContext.Create(budgets: [budget]);
        var handler = new UpdateBudgetLimitCommandHandler(db);

        Result result = await handler.Handle(
            new UpdateBudgetLimitCommand(budget.Id, 0m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.LimitMustBePositive);
        budget.MonthlyLimit.Amount.Should().Be(1_000m);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingBudget_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new UpdateBudgetLimitCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(
            new UpdateBudgetLimitCommand(missingId, 500m), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BudgetErrors.NotFound(missingId));
    }
}
