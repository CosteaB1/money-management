using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Budgets.GetBudgets;

/// <summary>
/// Returns the active budgets joined to their <c>BudgetPeriod</c> for the
/// given calendar month. <paramref name="Year"/> and <paramref name="Month"/>
/// both default to "now" (UTC) when null - the handler resolves that via the
/// injected <c>IDateTimeProvider</c>.
/// </summary>
public sealed record GetBudgetsQuery(int? Year = null, int? Month = null)
    : IQuery<IReadOnlyList<BudgetDto>>;
