using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.GetAccounts;

internal sealed class GetAccountsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetAccountsQuery, IReadOnlyList<AccountDto>>
{
    public async Task<Result<IReadOnlyList<AccountDto>>> Handle(
        GetAccountsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Account> accountsQuery = db.Accounts.AsQueryable();

        if (!query.IncludeArchived)
        {
            accountsQuery = accountsQuery.Where(a => !a.IsArchived);
        }

        List<Account> accounts = await accountsQuery
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        // Single aggregate query so the account list stays O(1) round-trips.
        // The global IsDeleted query filter on Transaction excludes soft-deleted
        // rows under EF Core; the explicit predicate here is defense-in-depth so
        // unit tests (which bypass the model configuration) behave identically.
        // Transfers, adjustments, and bank fees ALL count — every non-deleted
        // transaction is a real account movement. The maib parser splits its
        // combined `ieșiri` column into principal + fee at the source, so the
        // sum of all expense rows matches the bank's actual deductions and
        // reconciles against the per-row `Sold Disponibil` running balance.
        var txAggregates = await db.Transactions
            .Where(t => !t.IsDeleted)
            .GroupBy(t => new { t.AccountId, t.Direction })
            .Select(g => new
            {
                g.Key.AccountId,
                g.Key.Direction,
                Total = g.Sum(t => t.Amount.Amount),
            })
            .ToListAsync(cancellationToken);

        var incomeByAccount = txAggregates
            .Where(x => x.Direction == TransactionDirection.Income)
            .ToDictionary(x => x.AccountId, x => x.Total);

        var expenseByAccount = txAggregates
            .Where(x => x.Direction == TransactionDirection.Expense)
            .ToDictionary(x => x.AccountId, x => x.Total);

        // FxRate is a small reference table - one materialization per query is
        // cheaper than issuing N queries from inside the converter. The
        // identity case (MDL -> MDL) doesn't need a rate; everything else
        // resolves against this in-memory snapshot.
        List<FxRate> rates = await db.FxRates
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // The live balance is "now-valued" - the latest rate up to today best
        // approximates the dashboard reader's perspective.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var dtos = new List<AccountDto>(accounts.Count);
        foreach (Account account in accounts)
        {
            decimal income = incomeByAccount.GetValueOrDefault(account.Id, 0m);
            decimal expense = expenseByAccount.GetValueOrDefault(account.Id, 0m);
            decimal balance = account.Balance.Amount + income - expense;

            decimal? balanceMdl = ConvertInMemory(
                rates,
                balance,
                account.Balance.Currency,
                ReportingCurrencies.Mdl,
                today);

            dtos.Add(new AccountDto(
                account.Id,
                account.Name,
                account.Type,
                account.Balance.Currency,
                account.OpeningDate,
                account.IsArchived,
                account.Notes,
                balance,
                balanceMdl));
        }

        return Result.Success<IReadOnlyList<AccountDto>>(dtos);
    }

    /// <summary>
    /// Mirrors <see cref="MoneyManagement.Application.Abstractions.FxRates.IFxConverter"/>'s
    /// algorithm against an in-memory rate snapshot. Identity, direct, inverse,
    /// or <c>null</c> when no usable rate exists.
    /// </summary>
    private static decimal? ConvertInMemory(
        List<FxRate> rates,
        decimal amount,
        string fromCurrency,
        string toCurrency,
        DateOnly asOf)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.Ordinal))
        {
            return amount;
        }

        // Tie-break mirrors EfFxConverter: on a same (from, to, asOf) triple a
        // Manual row must win over BnmAuto, so the account list agrees with the
        // dashboard/reports (which all go through EfFxConverter). Ordering by an
        // explicit numeric key (Manual => 0) — never by r.Source directly.
        FxRate? direct = rates
            .Where(r =>
                r.FromCurrency == fromCurrency &&
                r.ToCurrency == toCurrency &&
                r.AsOf <= asOf)
            .OrderByDescending(r => r.AsOf)
            .ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)
            .FirstOrDefault();

        if (direct is not null)
        {
            return amount * direct.Rate;
        }

        FxRate? inverse = rates
            .Where(r =>
                r.FromCurrency == toCurrency &&
                r.ToCurrency == fromCurrency &&
                r.AsOf <= asOf)
            .OrderByDescending(r => r.AsOf)
            .ThenBy(r => r.Source == FxRateSource.Manual ? 0 : 1)
            .FirstOrDefault();

        if (inverse is not null && inverse.Rate > 0m)
        {
            return amount * (1m / inverse.Rate);
        }

        return null;
    }
}
