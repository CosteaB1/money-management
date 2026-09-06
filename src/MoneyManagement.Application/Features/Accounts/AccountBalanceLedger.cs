using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Accounts;

/// <summary>
/// One materialized snapshot of every non-deleted account movement, plus the
/// canonical "opening anchor + Σ income − Σ expense" balance formula evaluated
/// at an arbitrary as-of date.
/// <para>
/// The formula is hand-copied across roughly ten read handlers. This type is
/// the consolidation seam for the as-of callers — the net-worth card and the
/// net-worth trend — so the two numbers the dashboard shows side by side can
/// never drift apart. The remaining copies are left alone on purpose; folding
/// them in is a separate change.
/// </para>
/// <para>
/// Transfers, adjustments and bank fees ALL count. Net worth is balance
/// arithmetic, not P&amp;L — the opposite of the income/expense slices, which
/// filter transfers out. <c>NetWorthTrendFilterDisciplineTests</c> pins this.
/// </para>
/// </summary>
internal sealed class AccountBalanceLedger
{
    private readonly List<Movement> _movements;

    private AccountBalanceLedger(List<Movement> movements) => _movements = movements;

    /// <summary>
    /// Materializes every non-deleted movement in a single round-trip.
    /// <para>
    /// The explicit <c>!IsDeleted</c> predicate is defense-in-depth: EF Core's
    /// global query filter already excludes soft-deleted rows, but unit tests
    /// bypass model configuration and must behave identically.
    /// </para>
    /// </summary>
    public static async Task<AccountBalanceLedger> LoadAsync(
        IApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Select(t => new
            {
                t.AccountId,
                t.Direction,
                t.TransactionDate,
                AmountValue = t.Amount.Amount,
            })
            .ToListAsync(cancellationToken);

        var movements = new List<Movement>(rows.Count);
        foreach (var row in rows)
        {
            movements.Add(new Movement(row.AccountId, row.Direction, row.TransactionDate, row.AmountValue));
        }

        return new AccountBalanceLedger(movements);
    }

    /// <summary>
    /// The account's balance in its OWN currency, counting every movement dated
    /// on or before <paramref name="asOf"/>. FX is the caller's problem — the
    /// card converts at today, the trend at each point's own date, and mixing
    /// those has bitten this codebase before.
    /// </summary>
    public decimal NativeBalanceAsOf(Account account, DateOnly asOf)
    {
        decimal income = 0m;
        decimal expense = 0m;

        foreach (Movement movement in _movements)
        {
            if (movement.AccountId != account.Id)
            {
                continue;
            }
            if (movement.Date > asOf)
            {
                continue;
            }
            if (movement.Direction == TransactionDirection.Income)
            {
                income += movement.Amount;
            }
            else
            {
                expense += movement.Amount;
            }
        }

        return account.Balance.Amount + income - expense;
    }

    private readonly record struct Movement(
        Guid AccountId,
        TransactionDirection Direction,
        DateOnly Date,
        decimal Amount);
}
