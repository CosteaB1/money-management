using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Budgets;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using NSubstitute;

namespace MoneyManagement.Application.Tests.TestSupport;

/// <summary>
/// Hand-rolled fake of <see cref="IApplicationDbContext"/> backed by
/// <see cref="FakeDbSet{T}"/> instances. Skips EF Core configuration entirely,
/// so we sidestep the InMemory provider's lack of <c>ComplexProperty</c>
/// materialization while still supporting <c>ToListAsync</c>,
/// <c>FirstOrDefaultAsync</c>, and LINQ projection.
/// </summary>
internal static class FakeApplicationDbContext
{
    public static IApplicationDbContext Create(
        IEnumerable<Account>? accounts = null,
        IEnumerable<FxRate>? fxRates = null,
        IEnumerable<Category>? categories = null,
        IEnumerable<CategoryPattern>? categoryPatterns = null,
        IEnumerable<Transaction>? transactions = null,
        IEnumerable<ImportBatch>? imports = null,
        IEnumerable<Budget>? budgets = null,
        IEnumerable<BudgetPeriod>? budgetPeriods = null,
        IEnumerable<SavingsGoal>? savingsGoals = null,
        IEnumerable<SavingsGoalContribution>? savingsGoalContributions = null)
    {
        DbSet<Account> accountSet = new FakeDbSet<Account>(accounts ?? []);
        DbSet<FxRate> fxRateSet = new FakeDbSet<FxRate>(fxRates ?? []);
        DbSet<Category> categorySet = new FakeDbSet<Category>(categories ?? []);
        DbSet<CategoryPattern> categoryPatternSet = new FakeDbSet<CategoryPattern>(categoryPatterns ?? []);
        DbSet<Transaction> transactionSet = new FakeDbSet<Transaction>(transactions ?? []);
        DbSet<ImportBatch> importSet = new FakeDbSet<ImportBatch>(imports ?? []);
        DbSet<Budget> budgetSet = new FakeDbSet<Budget>(budgets ?? []);
        DbSet<BudgetPeriod> budgetPeriodSet = new FakeDbSet<BudgetPeriod>(budgetPeriods ?? []);
        DbSet<SavingsGoal> savingsGoalSet = new FakeDbSet<SavingsGoal>(savingsGoals ?? []);
        DbSet<SavingsGoalContribution> contributionSet =
            new FakeDbSet<SavingsGoalContribution>(savingsGoalContributions ?? []);

        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        db.Accounts.Returns(accountSet);
        db.FxRates.Returns(fxRateSet);
        db.Categories.Returns(categorySet);
        db.CategoryPatterns.Returns(categoryPatternSet);
        db.Transactions.Returns(transactionSet);
        db.ImportBatches.Returns(importSet);
        db.Budgets.Returns(budgetSet);
        db.BudgetPeriods.Returns(budgetPeriodSet);
        db.SavingsGoals.Returns(savingsGoalSet);
        db.SavingsGoalContributions.Returns(contributionSet);
        return db;
    }
}
