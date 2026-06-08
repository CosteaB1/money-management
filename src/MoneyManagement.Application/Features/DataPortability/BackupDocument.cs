using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.DataPortability;

/// <summary>
/// A round-trippable snapshot of the persisted rows in the database. Each
/// <c>*Backup</c> record below mirrors the persisted columns of its entity
/// exactly — including <c>Money</c> complex-property components, audit
/// timestamps, soft-delete / archive flags, FKs and enums (serialized as their
/// names by the API's <c>JsonStringEnumConverter</c>). The export deliberately
/// includes archived rows and soft-deleted transactions so a restore reproduces
/// those tables byte-for-byte (same IDs, same audit fields).
/// <para>
/// The <c>category_patterns</c> table (the learned/seeded auto-categorization
/// keyword rules) IS backed up — it is a child of <c>categories</c> via an
/// <c>ON DELETE CASCADE</c> FK, so a restore that wipes <c>categories</c> would
/// otherwise cascade-delete every pattern with no way to reinstate them.
/// </para>
/// <para>
/// The <c>fx_rates</c> table is deliberately EXCLUDED from the backup — rates are
/// re-fetchable from BNM, so they are neither exported nor touched on restore.
/// This is therefore no longer a literal snapshot of <em>every</em> persisted row.
/// </para>
/// </summary>
public sealed record BackupDocument(
    int SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    IReadOnlyList<AccountBackup> Accounts,
    IReadOnlyList<CategoryBackup> Categories,
    IReadOnlyList<CategoryPatternBackup> CategoryPatterns,
    IReadOnlyList<TransactionBackup> Transactions,
    IReadOnlyList<ImportBatchBackup> ImportBatches,
    IReadOnlyList<BudgetBackup> Budgets,
    IReadOnlyList<BudgetPeriodBackup> BudgetPeriods,
    IReadOnlyList<SavingsGoalBackup> SavingsGoals,
    IReadOnlyList<SavingsGoalContributionBackup> SavingsGoalContributions);

/// <summary>Mirrors the <c>accounts</c> table (<c>Balance</c> is the Money pair).</summary>
public sealed record AccountBackup(
    Guid Id,
    string Name,
    AccountType Type,
    decimal BalanceAmount,
    string BalanceCurrency,
    DateOnly OpeningDate,
    bool IsArchived,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Mirrors the <c>categories</c> table (self-referential <c>ParentId</c>).</summary>
public sealed record CategoryBackup(
    Guid Id,
    string Name,
    Guid? ParentId,
    string? Color,
    string? Icon,
    CategoryFlow Flow,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Mirrors the <c>category_patterns</c> table — the keyword-to-category rules
/// consumed by the import suggester. <c>Source</c> (Seeded/Learned) is serialized
/// as its name; <c>CategoryId</c> FKs <c>categories</c> (ON DELETE CASCADE), so on
/// restore these rows must be reinserted AFTER categories.
/// </summary>
public sealed record CategoryPatternBackup(
    Guid Id,
    string Keyword,
    Guid CategoryId,
    CategoryPatternSource Source,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Mirrors the <c>transactions</c> table (<c>Amount</c> is the Money pair; includes soft-deleted rows).</summary>
public sealed record TransactionBackup(
    Guid Id,
    Guid AccountId,
    Guid? CategoryId,
    DateOnly TransactionDate,
    TransactionDirection Direction,
    decimal AmountValue,
    string AmountCurrency,
    string Description,
    string? Notes,
    decimal? OriginalAmount,
    string? OriginalCurrency,
    TransactionSource Source,
    Guid? ImportBatchId,
    bool IsTransfer,
    Guid? CounterAccountId,
    bool IsAdjustment,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Mirrors the <c>import_batches</c> table.</summary>
public sealed record ImportBatchBackup(
    Guid Id,
    Guid AccountId,
    string FileName,
    string FileHash,
    BankSource BankSource,
    DateTime ImportedAt,
    int ImportedCount,
    int SkippedDuplicateCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Mirrors the <c>budgets</c> table (<c>MonthlyLimit</c> is the Money pair).</summary>
public sealed record BudgetBackup(
    Guid Id,
    Guid CategoryId,
    decimal MonthlyLimitAmount,
    string MonthlyLimitCurrency,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Mirrors the <c>budget_periods</c> table (<c>Spent</c> is the Money pair).</summary>
public sealed record BudgetPeriodBackup(
    Guid Id,
    Guid BudgetId,
    int Year,
    int Month,
    decimal SpentAmount,
    string SpentCurrency,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Mirrors the <c>savings_goals</c> table. <c>TargetAmount</c> is a Money pair;
/// the paired nullable <c>ManualSavedAmountValue</c> / <c>ManualSavedAmountCurrency</c>
/// columns are both NULL in linked mode and both populated in manual mode.
/// </summary>
public sealed record SavingsGoalBackup(
    Guid Id,
    string Name,
    decimal TargetAmountValue,
    string TargetAmountCurrency,
    DateOnly? TargetDate,
    Guid? LinkedAccountId,
    decimal? ManualSavedAmountValue,
    string? ManualSavedAmountCurrency,
    bool IsArchived,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Mirrors the <c>savings_goal_contributions</c> table (<c>Amount</c> is the Money pair).</summary>
public sealed record SavingsGoalContributionBackup(
    Guid Id,
    Guid GoalId,
    decimal AmountValue,
    string AmountCurrency,
    DateOnly OccurredOn,
    string? Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt);
