using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Imports;

public sealed record StatementPreviewDto(
    string FileHash,
    PeriodDto StatementPeriod,
    BankSource BankSource,
    SummaryDto Summary,
    IReadOnlyList<ParsedTransactionPreviewDto> Transactions);

public sealed record PeriodDto(string From, string To);

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
