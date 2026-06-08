using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MoneyManagement.Application.Abstractions.Backup;
using MoneyManagement.Application.Features.DataPortability;
using MoneyManagement.Infrastructure.Database;
using Npgsql;
using NpgsqlTypes;

namespace MoneyManagement.Infrastructure.Backup;

/// <summary>
/// EF Core / Npgsql-backed <see cref="IBackupStore"/>.
/// <para>
/// <b>Export</b> reads the backed-up <c>DbSet</c>s with <c>IgnoreQueryFilters()</c> +
/// <c>AsNoTracking()</c> so soft-deleted and archived rows are captured, then
/// projects to the flat <c>*Backup</c> records. The <c>category_patterns</c> table
/// (auto-categorization keyword rules) IS exported — it is a child of
/// <c>categories</c> via an <c>ON DELETE CASCADE</c> FK, so wiping categories on
/// restore would otherwise silently destroy every learned/seeded pattern. The
/// <c>fx_rates</c> table is intentionally NOT exported — rates are re-fetchable
/// from BNM.
/// </para>
/// <para>
/// <b>Restore</b> is destructive and transactional. It opens one transaction,
/// wipes the backed-up tables child-first (via <c>ExecuteDeleteAsync</c> with
/// <c>IgnoreQueryFilters()</c> so soft-deleted rows go too), then reinserts
/// parent-first. <c>category_patterns</c> is wiped before <c>categories</c> (its
/// FK parent) and reinserted after them. The <c>fx_rates</c> table is
/// intentionally left UNTOUCHED — it is neither wiped nor reinserted, so the
/// user's locally fetched rates survive a restore.
/// </para>
/// <para>
/// <b>Why raw parameterized INSERTs, not domain factories:</b> the restore must
/// preserve the EXACT original IDs, audit timestamps, and flags so FK
/// references and the snapshot round-trip. Routing rows through
/// <c>Account.Create</c> / <c>Transaction.Create</c> would mint fresh
/// <c>Guid.CreateVersion7()</c> IDs and rewrite audit fields, breaking every FK.
/// Tracked EF inserts would also fight the <c>AuditableEntitySaveChangesInterceptor</c>
/// (which stamps <c>created_at</c> / <c>updated_at</c>). Column names are pulled
/// from <c>context.Model</c> so they always track the EF configuration's
/// snake_case mapping and can't drift. Inserts run inside the same transaction
/// as the wipe; on any exception the transaction is disposed without commit and
/// Postgres rolls back, leaving the existing data untouched.
/// </para>
/// </summary>
internal sealed class EfBackupStore(ApplicationDbContext context) : IBackupStore
{
    public async Task<BackupDocument> ExportAsync(CancellationToken cancellationToken)
    {
        List<AccountBackup> accounts = await context.Accounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(a => new AccountBackup(
                a.Id,
                a.Name,
                a.Type,
                a.Balance.Amount,
                a.Balance.Currency,
                a.OpeningDate,
                a.IsArchived,
                a.Notes,
                a.CreatedAt,
                a.UpdatedAt))
            .ToListAsync(cancellationToken);

        List<CategoryBackup> categories = await context.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(c => new CategoryBackup(
                c.Id,
                c.Name,
                c.ParentId,
                c.Color,
                c.Icon,
                c.Flow,
                c.IsArchived,
                c.CreatedAt,
                c.UpdatedAt))
            .ToListAsync(cancellationToken);

        List<CategoryPatternBackup> categoryPatterns = await context.CategoryPatterns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(p => new CategoryPatternBackup(
                p.Id,
                p.Keyword,
                p.CategoryId,
                p.Source,
                p.CreatedAt,
                p.UpdatedAt))
            .ToListAsync(cancellationToken);

        List<TransactionBackup> transactions = await context.Transactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(t => new TransactionBackup(
                t.Id,
                t.AccountId,
                t.CategoryId,
                t.TransactionDate,
                t.Direction,
                t.Amount.Amount,
                t.Amount.Currency,
                t.Description,
                t.Notes,
                t.OriginalAmount,
                t.OriginalCurrency,
                t.Source,
                t.ImportBatchId,
                t.IsTransfer,
                t.CounterAccountId,
                t.IsAdjustment,
                t.IsDeleted,
                t.CreatedAt,
                t.UpdatedAt))
            .ToListAsync(cancellationToken);

        List<ImportBatchBackup> importBatches = await context.ImportBatches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(b => new ImportBatchBackup(
                b.Id,
                b.AccountId,
                b.FileName,
                b.FileHash,
                b.BankSource,
                b.ImportedAt,
                b.ImportedCount,
                b.SkippedDuplicateCount,
                b.CreatedAt,
                b.UpdatedAt))
            .ToListAsync(cancellationToken);

        List<BudgetBackup> budgets = await context.Budgets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(b => new BudgetBackup(
                b.Id,
                b.CategoryId,
                b.MonthlyLimit.Amount,
                b.MonthlyLimit.Currency,
                b.IsArchived,
                b.CreatedAt,
                b.UpdatedAt))
            .ToListAsync(cancellationToken);

        List<BudgetPeriodBackup> budgetPeriods = await context.BudgetPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(p => new BudgetPeriodBackup(
                p.Id,
                p.BudgetId,
                p.Year,
                p.Month,
                p.Spent.Amount,
                p.Spent.Currency,
                p.CreatedAt,
                p.UpdatedAt))
            .ToListAsync(cancellationToken);

        // ManualSavedAmount is persisted as two shadow-ish scalar columns the
        // entity reconstructs on read; project them via EF.Property so the
        // backup captures the raw paired columns (both NULL in linked mode).
        List<SavingsGoalBackup> savingsGoals = await context.SavingsGoals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(g => new SavingsGoalBackup(
                g.Id,
                g.Name,
                g.TargetAmount.Amount,
                g.TargetAmount.Currency,
                g.TargetDate,
                g.LinkedAccountId,
                EF.Property<decimal?>(g, "ManualSavedAmountValue"),
                EF.Property<string?>(g, "ManualSavedAmountCurrency"),
                g.IsArchived,
                g.CreatedAt,
                g.UpdatedAt))
            .ToListAsync(cancellationToken);

        List<SavingsGoalContributionBackup> savingsGoalContributions = await context.SavingsGoalContributions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(c => new SavingsGoalContributionBackup(
                c.Id,
                c.GoalId,
                c.Amount.Amount,
                c.Amount.Currency,
                c.OccurredOn,
                c.Notes,
                c.CreatedAt,
                c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new BackupDocument(
            BackupSchemaVersion.Current,
            DateTimeOffset.UtcNow,
            accounts,
            categories,
            categoryPatterns,
            transactions,
            importBatches,
            budgets,
            budgetPeriods,
            savingsGoals,
            savingsGoalContributions);
    }

    public async Task<ImportDataResult> RestoreAsync(BackupDocument document, CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        // Wipe child-first to respect FKs. ExecuteDeleteAsync issues a single
        // DELETE per table and bypasses the change tracker; IgnoreQueryFilters
        // ensures soft-deleted transactions and archived rows are removed too.
        await context.SavingsGoalContributions.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await context.SavingsGoals.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await context.BudgetPeriods.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await context.Budgets.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await context.Transactions.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await context.ImportBatches.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);

        // category_patterns.category_id is ON DELETE CASCADE, so deleting categories
        // below would silently wipe these too. Delete them explicitly first (child of
        // categories) so they are reinserted from the backup rather than lost.
        await context.CategoryPatterns.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);

        // categories.parent_id is ON DELETE RESTRICT (non-deferrable in Postgres, so
        // the check fires per-row mid-statement). Null every parent_id first so a
        // single DELETE FROM categories has no self-reference left to restrict.
        await context.Categories
            .IgnoreQueryFilters()
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ParentId, (Guid?)null), cancellationToken);
        await context.Categories.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        await context.Accounts.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);

        // fx_rates is intentionally NOT wiped — rates are re-fetchable from BNM,
        // so the user's locally fetched rates survive a restore.

        // Insert parent-first. Categories before everything that FKs them; the
        // self-referential parent_id is satisfied by ordering parents before
        // children within the batch (see InsertCategoriesAsync).
        int accounts = await InsertAccountsAsync(document.Accounts, cancellationToken);
        int categories = await InsertCategoriesAsync(document.Categories, cancellationToken);
        int categoryPatterns = await InsertCategoryPatternsAsync(document.CategoryPatterns, cancellationToken);
        int importBatches = await InsertImportBatchesAsync(document.ImportBatches, cancellationToken);
        int transactions = await InsertTransactionsAsync(document.Transactions, cancellationToken);
        int budgets = await InsertBudgetsAsync(document.Budgets, cancellationToken);
        int budgetPeriods = await InsertBudgetPeriodsAsync(document.BudgetPeriods, cancellationToken);
        int savingsGoals = await InsertSavingsGoalsAsync(document.SavingsGoals, cancellationToken);
        int savingsGoalContributions =
            await InsertSavingsGoalContributionsAsync(document.SavingsGoalContributions, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ImportDataResult(
            accounts,
            categories,
            categoryPatterns,
            transactions,
            importBatches,
            budgets,
            budgetPeriods,
            savingsGoals,
            savingsGoalContributions);
    }

    // ---- Inserts ---------------------------------------------------------
    // Column names come from context.Model so they track the EF configuration's
    // snake_case mapping; enums are stored as their .NET name (matching the
    // HasConversion<string>() config). Each row is one parameterized
    // ExecuteSqlRawAsync; rows are looped in document order.

    private async Task<int> InsertAccountsAsync(IReadOnlyList<AccountBackup> rows, CancellationToken ct)
    {
        string sql = InsertSql<Domain.Accounts.Account>(
            "id", "name", "type", "balance_amount", "balance_currency", "opening_date",
            "is_archived", "notes", "created_at", "updated_at");

        int count = 0;
        foreach (AccountBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.Name),
                    P(r.Type.ToString()),
                    P(r.BalanceAmount),
                    P(r.BalanceCurrency),
                    P(r.OpeningDate),
                    P(r.IsArchived),
                    PNullable(r.Notes),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertCategoriesAsync(IReadOnlyList<CategoryBackup> rows, CancellationToken ct)
    {
        // Order so that a parent is always inserted before its children — the
        // self-referential FK (parent_id) is checked per-row at insert time.
        IReadOnlyList<CategoryBackup> ordered = TopologicalByParent(rows);

        string sql = InsertSql<Domain.Categories.Category>(
            "id", "name", "parent_id", "color", "icon", "flow", "is_archived", "created_at", "updated_at");

        int count = 0;
        foreach (CategoryBackup r in ordered)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.Name),
                    PNullable(r.ParentId),
                    PNullable(r.Color),
                    PNullable(r.Icon),
                    P(r.Flow.ToString()),
                    P(r.IsArchived),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertCategoryPatternsAsync(
        IReadOnlyList<CategoryPatternBackup> rows,
        CancellationToken ct)
    {
        // Inserted after categories — the category_id FK (ON DELETE CASCADE) is
        // checked at insert time, so every referenced category must already exist.
        string sql = InsertSql<Domain.Categories.CategoryPattern>(
            "id", "keyword", "category_id", "source", "created_at", "updated_at");

        int count = 0;
        foreach (CategoryPatternBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.Keyword),
                    P(r.CategoryId),
                    P(r.Source.ToString()),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertImportBatchesAsync(IReadOnlyList<ImportBatchBackup> rows, CancellationToken ct)
    {
        string sql = InsertSql<Domain.Imports.ImportBatch>(
            "id", "account_id", "file_name", "file_hash", "bank_source", "imported_at",
            "imported_count", "skipped_duplicate_count", "created_at", "updated_at");

        int count = 0;
        foreach (ImportBatchBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.AccountId),
                    P(r.FileName),
                    P(r.FileHash),
                    P(r.BankSource.ToString()),
                    P(r.ImportedAt),
                    P(r.ImportedCount),
                    P(r.SkippedDuplicateCount),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertTransactionsAsync(IReadOnlyList<TransactionBackup> rows, CancellationToken ct)
    {
        string sql = InsertSql<Domain.Transactions.Transaction>(
            "id", "account_id", "category_id", "transaction_date", "direction", "amount_value", "amount_currency",
            "description", "notes", "original_amount", "original_currency", "source", "import_batch_id", "is_transfer",
            "counter_account_id", "is_adjustment", "is_deleted", "created_at", "updated_at");

        int count = 0;
        foreach (TransactionBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.AccountId),
                    PNullable(r.CategoryId),
                    P(r.TransactionDate),
                    P(r.Direction.ToString()),
                    P(r.AmountValue),
                    P(r.AmountCurrency),
                    P(r.Description),
                    PNullable(r.Notes),
                    PNullable(r.OriginalAmount),
                    PNullable(r.OriginalCurrency),
                    P(r.Source.ToString()),
                    PNullable(r.ImportBatchId),
                    P(r.IsTransfer),
                    PNullable(r.CounterAccountId),
                    P(r.IsAdjustment),
                    P(r.IsDeleted),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertBudgetsAsync(IReadOnlyList<BudgetBackup> rows, CancellationToken ct)
    {
        string sql = InsertSql<Domain.Budgets.Budget>(
            "id", "category_id", "monthly_limit_amount", "monthly_limit_currency", "is_archived",
            "created_at", "updated_at");

        int count = 0;
        foreach (BudgetBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.CategoryId),
                    P(r.MonthlyLimitAmount),
                    P(r.MonthlyLimitCurrency),
                    P(r.IsArchived),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertBudgetPeriodsAsync(IReadOnlyList<BudgetPeriodBackup> rows, CancellationToken ct)
    {
        string sql = InsertSql<Domain.Budgets.BudgetPeriod>(
            "id", "budget_id", "year", "month", "spent_amount", "spent_currency", "created_at", "updated_at");

        int count = 0;
        foreach (BudgetPeriodBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.BudgetId),
                    P(r.Year),
                    P(r.Month),
                    P(r.SpentAmount),
                    P(r.SpentCurrency),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertSavingsGoalsAsync(IReadOnlyList<SavingsGoalBackup> rows, CancellationToken ct)
    {
        string sql = InsertSql<Domain.SavingsGoals.SavingsGoal>(
            "id", "name", "target_amount_value", "target_amount_currency", "target_date", "linked_account_id",
            "manual_saved_amount_value", "manual_saved_amount_currency", "is_archived",
            "created_at", "updated_at");

        int count = 0;
        foreach (SavingsGoalBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.Name),
                    P(r.TargetAmountValue),
                    P(r.TargetAmountCurrency),
                    PNullable(r.TargetDate),
                    PNullable(r.LinkedAccountId),
                    PNullable(r.ManualSavedAmountValue),
                    PNullable(r.ManualSavedAmountCurrency),
                    P(r.IsArchived),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    private async Task<int> InsertSavingsGoalContributionsAsync(
        IReadOnlyList<SavingsGoalContributionBackup> rows,
        CancellationToken ct)
    {
        string sql = InsertSql<Domain.SavingsGoals.SavingsGoalContribution>(
            "id", "goal_id", "amount_value", "amount_currency", "occurred_on", "notes", "created_at", "updated_at");

        int count = 0;
        foreach (SavingsGoalContributionBackup r in rows)
        {
            count += await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    P(r.Id),
                    P(r.GoalId),
                    P(r.AmountValue),
                    P(r.AmountCurrency),
                    P(r.OccurredOn),
                    PNullable(r.Notes),
                    P(r.CreatedAt),
                    P(r.UpdatedAt),
                ],
                ct);
        }

        return count;
    }

    // ---- Helpers ---------------------------------------------------------

    /// <summary>
    /// Builds <c>INSERT INTO "table" (cols) VALUES ({0}, {1}, ...)</c> for raw
    /// parameterized execution. The table name comes from the EF model (so it
    /// tracks the snake_case mapping); the column names are passed by the caller
    /// and MUST match the EF configuration. Both are trusted, app-controlled
    /// identifiers — the row VALUES are always bound parameters, never
    /// interpolated.
    /// </summary>
    private string InsertSql<TEntity>(params string[] columns)
        where TEntity : class
    {
        IEntityType entityType =
            context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"No EF entity type for {typeof(TEntity).Name}.");

        string tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"No table name for {typeof(TEntity).Name}.");
        string? schema = entityType.GetSchema();

        var builder = new StringBuilder("INSERT INTO ");
        if (!string.IsNullOrEmpty(schema))
        {
            builder.Append('"').Append(schema).Append("\".");
        }

        builder.Append('"').Append(tableName).Append("\" (");
        builder.AppendJoin(", ", columns);
        builder.Append(") VALUES (");
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append('{').Append(i).Append('}');
        }

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>
    /// Orders categories so every parent precedes its children. The self-ref FK
    /// (parent_id, ON DELETE RESTRICT) is checked per-row at INSERT, so a child
    /// inserted before its parent would violate it.
    /// </summary>
    private static IReadOnlyList<CategoryBackup> TopologicalByParent(IReadOnlyList<CategoryBackup> rows)
    {
        var byId = rows.ToDictionary(c => c.Id);
        var ordered = new List<CategoryBackup>(rows.Count);
        var visited = new HashSet<Guid>();

        void Visit(CategoryBackup category)
        {
            if (!visited.Add(category.Id))
            {
                return;
            }

            if (category.ParentId is { } parentId && byId.TryGetValue(parentId, out CategoryBackup? parent))
            {
                Visit(parent);
            }

            ordered.Add(category);
        }

        foreach (CategoryBackup category in rows)
        {
            Visit(category);
        }

        return ordered;
    }

    private static NpgsqlParameter P(Guid value) => new() { Value = value };

    private static NpgsqlParameter P(string value) => new() { Value = value };

    private static NpgsqlParameter P(bool value) => new() { Value = value };

    private static NpgsqlParameter P(int value) => new() { Value = value };

    private static NpgsqlParameter P(decimal value) => new() { Value = value };

    // All DateTime columns are timestamptz; Npgsql requires Kind=Utc. EF reads
    // them back as Utc, and a Z-suffixed JSON value round-trips as Utc, but a
    // hand-edited backup could carry Unspecified — coerce defensively.
    private static NpgsqlParameter P(DateTime value) =>
        new()
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private static NpgsqlParameter P(DateOnly value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Date, Value = value };

    private static NpgsqlParameter PNullable(string? value) =>
        new() { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter PNullable(Guid? value) =>
        new() { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter PNullable(decimal? value) =>
        new() { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter PNullable(DateOnly? value) =>
        new() { NpgsqlDbType = NpgsqlDbType.Date, Value = (object?)value ?? DBNull.Value };
}
