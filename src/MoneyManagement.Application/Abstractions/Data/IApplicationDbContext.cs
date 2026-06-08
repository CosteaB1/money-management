using Microsoft.EntityFrameworkCore;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<Account> Accounts { get; }
    DbSet<Category> Categories { get; }
    DbSet<CategoryPattern> CategoryPatterns { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<FxRate> FxRates { get; }
    DbSet<Budget> Budgets { get; }
    DbSet<BudgetPeriod> BudgetPeriods { get; }
    DbSet<SavingsGoal> SavingsGoals { get; }
    DbSet<SavingsGoalContribution> SavingsGoalContributions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
