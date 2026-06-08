using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.SavingsGoals.GetGoals;

internal sealed class GetGoalsQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock) : IQueryHandler<GetGoalsQuery, IReadOnlyList<GoalDto>>
{
    public async Task<Result<IReadOnlyList<GoalDto>>> Handle(
        GetGoalsQuery query,
        CancellationToken cancellationToken)
    {
        // The is_archived = false global query filter excludes archived goals
        // under EF Core; the explicit predicate is defense-in-depth so unit
        // tests (which bypass model configuration) exercise the same rule.
        List<SavingsGoal> goals = await db.SavingsGoals
            .Where(g => !g.IsArchived)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(cancellationToken);

        if (goals.Count == 0)
        {
            return Result.Success<IReadOnlyList<GoalDto>>([]);
        }

        // Pull just the accounts referenced by linked goals so we can compute
        // their live MDL balance. AnyAsync would be cheaper for the existence
        // check but we need the row itself (currency + anchor) to compute the
        // balance, so a single materialization is the right shape.
        Guid[] linkedAccountIds = goals
            .Where(g => g.LinkedAccountId is not null)
            .Select(g => g.LinkedAccountId!.Value)
            .Distinct()
            .ToArray();

        Dictionary<Guid, Account> accountsById;
        Dictionary<Guid, decimal> incomeByAccount;
        Dictionary<Guid, decimal> expenseByAccount;

        if (linkedAccountIds.Length > 0)
        {
            List<Account> linkedAccounts = await db.Accounts
                .Where(a => linkedAccountIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            accountsById = linkedAccounts.ToDictionary(a => a.Id);

            // Same aggregate shape as GetAccountsQueryHandler so the balance
            // computation matches the dashboard's account list. Every
            // non-deleted row counts; the explicit !IsDeleted predicate is
            // defense-in-depth since unit tests bypass the EF query filter.
            var txAggregates = await db.Transactions
                .Where(t => !t.IsDeleted && linkedAccountIds.Contains(t.AccountId))
                .GroupBy(t => new { t.AccountId, t.Direction })
                .Select(g => new
                {
                    g.Key.AccountId,
                    g.Key.Direction,
                    Total = g.Sum(t => t.Amount.Amount),
                })
                .ToListAsync(cancellationToken);

            incomeByAccount = txAggregates
                .Where(x => x.Direction == TransactionDirection.Income)
                .ToDictionary(x => x.AccountId, x => x.Total);

            expenseByAccount = txAggregates
                .Where(x => x.Direction == TransactionDirection.Expense)
                .ToDictionary(x => x.AccountId, x => x.Total);
        }
        else
        {
            accountsById = [];
            incomeByAccount = [];
            expenseByAccount = [];
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        var dtos = new List<GoalDto>(goals.Count);

        foreach (SavingsGoal goal in goals)
        {
            decimal saved;
            string? linkedAccountName = null;
            bool missingFxRate = false;

            if (goal.LinkedAccountId is Guid linkedId
                && accountsById.TryGetValue(linkedId, out Account? account))
            {
                linkedAccountName = account.Name;
                decimal income = incomeByAccount.GetValueOrDefault(account.Id, 0m);
                decimal expense = expenseByAccount.GetValueOrDefault(account.Id, 0m);
                decimal nativeBalance = account.Balance.Amount + income - expense;

                decimal? mdl = await fxConverter.ConvertAsync(
                    nativeBalance,
                    account.Balance.Currency,
                    ReportingCurrencies.Mdl,
                    today,
                    cancellationToken);

                if (mdl is null)
                {
                    // The linked account exists but we have no rate for its
                    // currency on or before today - surface that so the UI
                    // can prompt the user to add one.
                    saved = 0m;
                    missingFxRate = true;
                }
                else
                {
                    saved = mdl.Value;
                }
            }
            else if (goal.LinkedAccountId is not null)
            {
                // Dangling LinkedAccountId - the FK restriction on the
                // account table makes this unreachable in production, but
                // a defensive zero keeps the page from blowing up if a
                // future code path ever drops the constraint.
                saved = 0m;
            }
            else
            {
                // Manual mode - the factory guarantees ManualSavedAmount is
                // set when LinkedAccountId is null. Defensive ?? 0m keeps
                // the projection well-typed against the nullable field.
                saved = goal.ManualSavedAmount?.Amount ?? 0m;
            }

            GoalProjection.Projection projection = GoalProjection.Project(goal, saved, today);

            dtos.Add(new GoalDto(
                goal.Id,
                goal.Name,
                goal.TargetAmount.Amount,
                goal.TargetDate,
                goal.LinkedAccountId,
                linkedAccountName,
                saved,
                projection.Remaining,
                projection.ProgressPercent,
                projection.Status,
                projection.RequiredMonthlyContribution,
                IsLinkedMode: goal.LinkedAccountId is not null,
                MissingFxRate: missingFxRate));
        }

        return Result.Success<IReadOnlyList<GoalDto>>(dtos);
    }
}
