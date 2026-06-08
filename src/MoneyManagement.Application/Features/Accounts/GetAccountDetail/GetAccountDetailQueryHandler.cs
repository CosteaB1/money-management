using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.GetAccountDetail;

/// <summary>
/// Returns per-account detail used by the frontend's `/accounts/{id}` page.
/// Decomposes account activity into contributions (inbound transfer legs),
/// withdrawals (outbound transfer legs), and net P&amp;L (balance adjustments
/// signed by direction). Mirrors <c>GetSummaryQueryHandler</c>'s row-date FX
/// convention — each row converts at its own <c>TransactionDate</c>.
/// </summary>
internal sealed class GetAccountDetailQueryHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter,
    IDateTimeProvider clock)
    : IQueryHandler<GetAccountDetailQuery, AccountDetailDto>
{
    public async Task<Result<AccountDetailDto>> Handle(
        GetAccountDetailQuery query,
        CancellationToken cancellationToken)
    {
        // Archived accounts must remain reachable here so the user can drill
        // into a closed account's history — IgnoreQueryFilters bypasses the
        // global IsArchived filter on Account.
        Account? account = await db.Accounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == query.Id, cancellationToken);

        if (account is null)
        {
            return Result.Failure<AccountDetailDto>(AccountErrors.NotFound(query.Id));
        }

        // The global IsDeleted query filter excludes soft-deleted rows under EF
        // Core; the explicit predicate is defense-in-depth so unit tests (which
        // bypass model configuration) behave identically.
        var rows = await db.Transactions
            .Where(t => !t.IsDeleted)
            .Where(t => t.AccountId == account.Id)
            .Select(t => new
            {
                t.TransactionDate,
                t.Direction,
                t.IsTransfer,
                t.IsAdjustment,
                AmountValue = t.Amount.Amount,
                AmountCurrency = t.Amount.Currency,
            })
            .ToListAsync(cancellationToken);

        // Bucketing rules (see WIKI "Known rough edges" for the canonical
        // rationale on the IsTransfer/IsAdjustment classifier):
        //   IsTransfer && Income  -> contribution (inbound transfer leg)
        //   IsTransfer && Expense -> withdrawal  (outbound transfer leg)
        //   IsAdjustment && Income  -> +P&L
        //   IsAdjustment && Expense -> -P&L
        //   else -> real activity (counted, not summed into any bucket)
        // is_transfer and is_adjustment are mutually exclusive at the domain
        // layer (TransactionErrors.TransferAndAdjustmentAreMutuallyExclusive),
        // so the branches never overlap.
        DateTime now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var yearStart = new DateOnly(today.Year, 1, 1);

        decimal incomeNative = 0m;
        decimal expenseNative = 0m;

        decimal allContrib = 0m, allWithdraw = 0m, allPnL = 0m;
        int allContribCount = 0, allWithdrawCount = 0, allAdjCount = 0;
        bool allMissing = false;

        decimal ytdContrib = 0m, ytdWithdraw = 0m, ytdPnL = 0m;
        int ytdContribCount = 0, ytdWithdrawCount = 0, ytdAdjCount = 0;
        bool ytdMissing = false;

        DateOnly? firstActivity = null;
        DateOnly? lastActivity = null;
        int realActivityCount = 0;

        foreach (var row in rows)
        {
            // Live balance arithmetic mirrors GetAccountsQueryHandler — every
            // non-deleted row moves the per-account balance, transfers and
            // adjustments included.
            if (row.Direction == TransactionDirection.Income)
            {
                incomeNative += row.AmountValue;
            }
            else
            {
                expenseNative += row.AmountValue;
            }

            if (firstActivity is null || row.TransactionDate < firstActivity)
            {
                firstActivity = row.TransactionDate;
            }

            if (lastActivity is null || row.TransactionDate > lastActivity)
            {
                lastActivity = row.TransactionDate;
            }

            bool inYtd = row.TransactionDate >= yearStart && row.TransactionDate <= today;

            if (!row.IsTransfer && !row.IsAdjustment)
            {
                realActivityCount++;
                continue;
            }

            decimal? mdl = await fxConverter.ConvertAsync(
                row.AmountValue,
                row.AmountCurrency,
                ReportingCurrencies.Mdl,
                row.TransactionDate,
                cancellationToken);

            if (row.IsTransfer)
            {
                if (row.Direction == TransactionDirection.Income)
                {
                    allContribCount++;
                    if (inYtd)
                    {
                        ytdContribCount++;
                    }

                    if (mdl is null)
                    {
                        allMissing = true;
                        if (inYtd)
                        {
                            ytdMissing = true;
                        }
                    }
                    else
                    {
                        allContrib += mdl.Value;
                        if (inYtd)
                        {
                            ytdContrib += mdl.Value;
                        }
                    }
                }
                else
                {
                    allWithdrawCount++;
                    if (inYtd)
                    {
                        ytdWithdrawCount++;
                    }

                    if (mdl is null)
                    {
                        allMissing = true;
                        if (inYtd)
                        {
                            ytdMissing = true;
                        }
                    }
                    else
                    {
                        allWithdraw += mdl.Value;
                        if (inYtd)
                        {
                            ytdWithdraw += mdl.Value;
                        }
                    }
                }
            }
            else
            {
                // IsAdjustment branch (mutual exclusivity guarantees this).
                allAdjCount++;
                if (inYtd)
                {
                    ytdAdjCount++;
                }

                if (mdl is null)
                {
                    allMissing = true;
                    if (inYtd)
                    {
                        ytdMissing = true;
                    }
                }
                else
                {
                    decimal signed = row.Direction == TransactionDirection.Income
                        ? mdl.Value
                        : -mdl.Value;
                    allPnL += signed;
                    if (inYtd)
                    {
                        ytdPnL += signed;
                    }
                }
            }
        }

        decimal balance = account.Balance.Amount + incomeNative - expenseNative;

        decimal? balanceMdl = await fxConverter.ConvertAsync(
            balance,
            account.Balance.Currency,
            ReportingCurrencies.Mdl,
            today,
            cancellationToken);

        var allTime = new AccountActivityTotalsDto(
            allContrib,
            allWithdraw,
            allPnL,
            allContribCount,
            allWithdrawCount,
            allAdjCount,
            allMissing);

        var ytd = new AccountActivityTotalsDto(
            ytdContrib,
            ytdWithdraw,
            ytdPnL,
            ytdContribCount,
            ytdWithdrawCount,
            ytdAdjCount,
            ytdMissing);

        return Result.Success(new AccountDetailDto(
            account.Id,
            account.Name,
            account.Type,
            account.Balance.Currency,
            account.OpeningDate,
            account.IsArchived,
            account.Notes,
            balance,
            balanceMdl,
            account.Balance.Amount,
            allTime,
            ytd,
            firstActivity,
            lastActivity,
            realActivityCount));
    }
}
