namespace MoneyManagement.Application.Features.DataPortability;

/// <summary>
/// Per-table row counts inserted by a restore. Surfaced to the caller so the
/// frontend can report exactly what landed after a destructive full-replace.
/// </summary>
public sealed record ImportDataResult(
    int Accounts,
    int Categories,
    int CategoryPatterns,
    int Transactions,
    int ImportBatches,
    int Budgets,
    int BudgetPeriods,
    int SavingsGoals,
    int SavingsGoalContributions);
