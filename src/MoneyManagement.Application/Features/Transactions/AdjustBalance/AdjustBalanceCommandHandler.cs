using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Transactions.AdjustBalance;

internal sealed class AdjustBalanceCommandHandler(
    IApplicationDbContext db,
    IFxConverter fxConverter)
    : ICommandHandler<AdjustBalanceCommand, AdjustBalanceResult>
{
    private const string AdjustmentDescription = "Balance adjustment";
    private const string InvestmentDescription = "Investment";
    private const string WithdrawalDescription = "Withdrawal";

    /// <summary>
    /// Account types whose balance is meaningful only at coarse cadence (the
    /// user can't enumerate per-trade transactions). Cash / current-account /
    /// credit-card balances are derived from full transaction history and
    /// reject manual balance changes.
    /// </summary>
    private static readonly HashSet<AccountType> EligibleTypes =
    [
        AccountType.Brokerage,
        AccountType.CryptoExchange,
        AccountType.P2PLending,
        AccountType.BankDeposit,
    ];

    public async Task<Result<AdjustBalanceResult>> Handle(
        AdjustBalanceCommand command,
        CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);

        if (account is null)
        {
            return Result.Failure<AdjustBalanceResult>(AccountErrors.NotFound(command.AccountId));
        }

        if (!EligibleTypes.Contains(account.Type))
        {
            return Result.Failure<AdjustBalanceResult>(
                TransactionErrors.AdjustmentAccountTypeNotEligible(account.Type.ToString()));
        }

        string accountCurrency = account.Balance.Currency;

        // Resolve the per-kind shape of the transaction to write. Adjustment
        // computes its delta from the live balance; Investment/Withdrawal take
        // the supplied amount verbatim.
        Result<BalanceChange> changeResult = command.Kind switch
        {
            BalanceChangeKind.Adjustment =>
                await ResolveAdjustmentAsync(command, account, cancellationToken),
            BalanceChangeKind.Investment => Result.Success(new BalanceChange(
                TransactionDirection.Income,
                command.Value,
                SeededCategories.InvestmentId,
                IsTransfer: true,
                IsAdjustment: false,
                Delta: command.Value,
                InvestmentDescription)),
            BalanceChangeKind.Withdrawal => Result.Success(new BalanceChange(
                TransactionDirection.Expense,
                command.Value,
                SeededCategories.WithdrawalId,
                IsTransfer: true,
                IsAdjustment: false,
                Delta: -command.Value,
                WithdrawalDescription)),
            _ => Result.Failure<BalanceChange>(TransactionErrors.InvalidDirection),
        };

        if (changeResult.IsFailure)
        {
            return Result.Failure<AdjustBalanceResult>(changeResult.Error);
        }

        BalanceChange change = changeResult.Value;

        var money = new Money(change.Money, accountCurrency);
        // Description is always the kind's default label ("Investment" /
        // "Withdrawal" / "Balance adjustment"). The user's free text is the
        // transaction's NOTES — not the description — consistent with the
        // manual-transaction and import flows.
        string description = change.DefaultDescription;

        // Adjustments don't count toward budget spend (the event handler skips
        // IsAdjustment rows), but the MDL value still flows through the event
        // for any future consumers. Investment/Withdrawal are transfer-flagged
        // and are likewise excluded from budget spend.
        decimal? amountMdl = await fxConverter.ConvertAsync(
            money.Amount,
            accountCurrency,
            ReportingCurrencies.Mdl,
            command.Date,
            cancellationToken);

        Result<Transaction> txResult = Transaction.Create(
            command.AccountId,
            command.Date,
            change.Direction,
            money,
            description,
            TransactionSource.Manual,
            categoryId: change.CategoryId,
            importBatchId: null,
            originalAmount: null,
            originalCurrency: null,
            isTransfer: change.IsTransfer,
            counterAccountId: null,
            isAdjustment: change.IsAdjustment,
            amountMdl: amountMdl,
            notes: command.Notes);

        if (txResult.IsFailure)
        {
            return Result.Failure<AdjustBalanceResult>(txResult.Error);
        }

        Transaction transaction = txResult.Value;
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync(cancellationToken);

        return new AdjustBalanceResult(transaction.Id, change.Delta);
    }

    private async Task<Result<BalanceChange>> ResolveAdjustmentAsync(
        AdjustBalanceCommand command,
        Account account,
        CancellationToken cancellationToken)
    {
        string accountCurrency = account.Balance.Currency;

        // The Phase 4 invariant (Transaction.Amount.Currency == Account.Balance.Currency)
        // is enforced going forward on every write path. Existing rows pre-date
        // that guarantee; assert in debug builds to catch any drift.
        //
        // Every non-deleted transaction on the account contributes to the
        // current balance — same definition used by GetAccountsQueryHandler.
        // The maib parser splits combined ieșiri rows into principal + fee at
        // the source, so fees are real expenses and must be summed here too.
        var transactionsOnAccount = await db.Transactions
            .Where(t => t.AccountId == command.AccountId)
            .Select(t => new { t.Direction, AmountValue = t.Amount.Amount, AmountCurrency = t.Amount.Currency })
            .ToListAsync(cancellationToken);

        Debug.Assert(
            transactionsOnAccount.TrueForAll(t => t.AmountCurrency == accountCurrency),
            "All transactions on an account must share its currency (Phase 4 invariant).");

        decimal currentBalance = account.Balance.Amount
            + transactionsOnAccount
                .Where(t => t.Direction == TransactionDirection.Income)
                .Sum(t => t.AmountValue)
            - transactionsOnAccount
                .Where(t => t.Direction == TransactionDirection.Expense)
                .Sum(t => t.AmountValue);

        decimal delta = command.Value - currentBalance;

        if (delta == 0m)
        {
            return Result.Failure<BalanceChange>(TransactionErrors.AdjustmentDeltaZero);
        }

        TransactionDirection direction = delta > 0m
            ? TransactionDirection.Income
            : TransactionDirection.Expense;

        return Result.Success(new BalanceChange(
            direction,
            Math.Abs(delta),
            SeededCategories.BalanceAdjustmentId,
            IsTransfer: false,
            IsAdjustment: true,
            Delta: delta,
            AdjustmentDescription));
    }

    /// <summary>
    /// The resolved per-kind shape of the transaction to write, plus the
    /// signed <see cref="Delta"/> reported back to the caller.
    /// </summary>
    private readonly record struct BalanceChange(
        TransactionDirection Direction,
        decimal Money,
        Guid CategoryId,
        bool IsTransfer,
        bool IsAdjustment,
        decimal Delta,
        string DefaultDescription);
}
