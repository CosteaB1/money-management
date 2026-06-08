using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Imports.CommitImport;

public sealed record CommitImportCommand(
    Guid AccountId,
    string FileName,
    string FileHash,
    BankSource BankSource,
    IReadOnlyList<TransactionToImport> Transactions,
    IReadOnlyList<LearnedCategoryPattern>? LearnedPatterns = null) : ICommand<CommitResultDto>;

public sealed record TransactionToImport(
    DateOnly TransactionDate,
    TransactionDirection Direction,
    decimal Amount,
    string Description,
    Guid? CategoryId,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    bool IsTransfer = false,
    Guid? CounterAccountId = null,
    decimal? CounterAmount = null,
    string? Notes = null);

/// <summary>
/// A keyword the user chose to remember for a category during an import. Upserted
/// (best-effort) as a <c>Learned</c> <c>CategoryPattern</c> so it influences the
/// category suggester on FUTURE imports.
/// </summary>
public sealed record LearnedCategoryPattern(string Keyword, Guid CategoryId);
