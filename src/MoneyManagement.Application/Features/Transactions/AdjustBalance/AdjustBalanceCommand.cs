using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Transactions.AdjustBalance;

/// <summary>
/// The three ways the balance of an investment-style account can change without
/// per-trade transaction history:
/// <list type="bullet">
/// <item><see cref="Adjustment"/> - re-marks the account to a NEW TOTAL balance
/// (P&amp;L). The handler computes the delta against the current balance and
/// writes an <c>IsAdjustment</c> income/expense row.</item>
/// <item><see cref="Investment"/> - money moved INTO the account. The
/// <c>Value</c> is the amount added; written as a transfer-flagged income
/// row.</item>
/// <item><see cref="Withdrawal"/> - money moved OUT of the account. The
/// <c>Value</c> is the amount removed; written as a transfer-flagged expense
/// row.</item>
/// </list>
/// </summary>
public enum BalanceChangeKind
{
    Adjustment,
    Investment,
    Withdrawal,
}

/// <summary>
/// Records a balance change against an investment-style account.
/// </summary>
/// <param name="AccountId">The target account.</param>
/// <param name="Kind">Which of the three balance-change modes to apply.</param>
/// <param name="Value">
/// Semantics depend on <paramref name="Kind"/>:
/// for <see cref="BalanceChangeKind.Adjustment"/> this is the NEW TOTAL balance
/// (the delta against the current balance is computed by the handler and may be
/// any sign);
/// for <see cref="BalanceChangeKind.Investment"/> and
/// <see cref="BalanceChangeKind.Withdrawal"/> this is the AMOUNT moved and must
/// be greater than 0.
/// </param>
/// <param name="Date">The date the change occurred (not in the future).</param>
/// <param name="Notes">Optional description; falls back to a per-kind default.</param>
public sealed record AdjustBalanceCommand(
    Guid AccountId,
    BalanceChangeKind Kind,
    decimal Value,
    DateOnly Date,
    string? Notes) : ICommand<AdjustBalanceResult>;

public sealed record AdjustBalanceResult(Guid TransactionId, decimal Delta);
