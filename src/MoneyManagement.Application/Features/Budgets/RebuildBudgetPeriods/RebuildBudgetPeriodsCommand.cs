using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Budgets.RebuildBudgetPeriods;

/// <summary>
/// Recomputes <see cref="MoneyManagement.Domain.Budgets.BudgetPeriod"/> rows
/// from the underlying transactions. The canonical correction path for any
/// budget-spend drift accumulated before the inverse event handlers landed.
/// <see cref="BudgetId"/> = <c>null</c> targets every active budget.
/// </summary>
public sealed record RebuildBudgetPeriodsCommand(Guid? BudgetId)
    : ICommand<RebuildBudgetPeriodsResult>;

public sealed record RebuildBudgetPeriodsResult(int BudgetsRebuilt, int PeriodsAffected);
