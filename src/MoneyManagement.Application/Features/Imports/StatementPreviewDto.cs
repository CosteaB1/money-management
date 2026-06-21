using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Imports;

public sealed record StatementPreviewDto(
    string FileHash,
    PeriodDto StatementPeriod,
    BankSource BankSource,
    SummaryDto Summary,
    ReconciliationDto Reconciliation,
    IReadOnlyList<ParsedTransactionPreviewDto> Transactions);

public sealed record PeriodDto(string From, string To);

/// <summary>
/// Non-blocking opening-balance reconciliation signal computed at parse time.
/// Compares the statement's printed opening balance against the app's computed
/// balance just before the statement period to surface a month-boundary data gap.
/// All amounts are in the account's native currency (MAIB statements are MDL; no FX).
/// </summary>
public sealed record ReconciliationDto(
    decimal StatementOpeningBalance,    // = Summary.OpeningBalance (MAIB "Sold inițial")
    decimal AppBalanceBeforeStatement,  // balanceAsOf(Period.From − 1), account currency
    decimal OpeningDelta,               // StatementOpeningBalance − AppBalanceBeforeStatement
    bool OpeningMatches);               // |OpeningDelta| <= tolerance

/// <summary>
/// Reconciliation tolerances shared between the parse handler and its tests.
/// </summary>
public static class ImportReconciliation
{
    /// <summary>Maximum absolute opening-balance delta (account minor units) still treated as a match.</summary>
    public const decimal ToleranceMinor = 0.01m;
}

public sealed record SummaryDto(
    decimal OpeningBalance,
    decimal ClosingBalance,
    decimal TotalIn,
    decimal TotalOut,
    decimal TotalFees);

public sealed record ParsedTransactionPreviewDto(
    DateOnly TransactionDate,
    TransactionDirection Direction,
    decimal Amount,
    string Description,
    Guid? SuggestedCategoryId,
    string? SuggestedCategoryName,
    bool IsDuplicate,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    bool IsTransfer);
