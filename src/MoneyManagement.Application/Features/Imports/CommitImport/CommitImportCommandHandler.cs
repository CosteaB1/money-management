using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Imports;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Imports.CommitImport;

internal sealed class CommitImportCommandHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    IFxConverter fxConverter) : ICommandHandler<CommitImportCommand, CommitResultDto>
{
    public async Task<Result<CommitResultDto>> Handle(
        CommitImportCommand command,
        CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.AccountId, cancellationToken);

        if (account is null)
        {
            return Result.Failure<CommitResultDto>(AccountErrors.NotFound(command.AccountId));
        }

        if (command.Transactions.Count == 0)
        {
            return Result.Failure<CommitResultDto>(
                Error.Validation("imports.empty_batch", "Commit must include at least one transaction."));
        }

        DateOnly fromDate = command.Transactions.Min(t => t.TransactionDate);
        DateOnly toDate = command.Transactions.Max(t => t.TransactionDate);
        string accountCurrency = account.Balance.Currency;

        // Counter accounts are optional. When the user picks one for a transfer
        // row, we create a paired leg on that account. Useful for ATM-to-Cash,
        // brokerage funding, and similar destinations that don't produce PDFs.
        Guid[] counterAccountIds = [.. command.Transactions
            .Where(t => t.IsTransfer && t.CounterAccountId is not null)
            .Select(t => t.CounterAccountId!.Value)
            .Distinct()];

        Dictionary<Guid, Account> counterAccounts = new();
        if (counterAccountIds.Length > 0)
        {
            List<Account> loaded = await db.Accounts
                .Where(a => counterAccountIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

            counterAccounts = loaded.ToDictionary(a => a.Id);

            foreach (Guid counterId in counterAccountIds)
            {
                if (!counterAccounts.TryGetValue(counterId, out Account? counter))
                {
                    return Result.Failure<CommitResultDto>(AccountErrors.NotFound(counterId));
                }

                if (counter.Id == command.AccountId)
                {
                    return Result.Failure<CommitResultDto>(TransactionErrors.CounterAccountCannotBeSelf);
                }

                if (counter.IsArchived)
                {
                    return Result.Failure<CommitResultDto>(AccountErrors.IsArchived(counter.Id));
                }
            }
        }

        var existing = await db.Transactions
            .Where(t => t.AccountId == command.AccountId
                && t.TransactionDate >= fromDate
                && t.TransactionDate <= toDate)
            .Select(t => new
            {
                t.TransactionDate,
                t.Amount,
                t.Description,
                t.IsTransfer,
                t.Direction,
            })
            .ToListAsync(cancellationToken);

        var existingSignatures = existing
            .Select(t => DuplicateSignature.Compute(command.AccountId, t.TransactionDate, t.Amount.Amount, t.Description))
            .ToHashSet(StringComparer.Ordinal);

        // Transfer-aware dedup: maib statements contain both sides of an A2A
        // transfer with different descriptions ("A2A de iesire ..." vs.
        // "A2A de intrare ..."), so signature-based dedup misses the second
        // statement on re-import. Match on (date, amount, direction) for rows
        // both sides marked as transfers.
        var existingTransferLegs = existing
            .Where(t => t.IsTransfer)
            .Select(t => (t.TransactionDate, t.Amount.Amount, t.Direction))
            .ToHashSet();

        // Counter-side dedup: prevent creating a matching leg that duplicates
        // a row already present on the counter account.
        Dictionary<Guid, HashSet<(DateOnly, decimal, TransactionDirection)>> existingCounterLegs = new();
        foreach (Guid counterId in counterAccountIds)
        {
            List<(DateOnly Date, decimal Amount, TransactionDirection Direction)> rows = await db.Transactions
                .Where(t => t.AccountId == counterId
                    && t.TransactionDate >= fromDate
                    && t.TransactionDate <= toDate)
                .Select(t => new ValueTuple<DateOnly, decimal, TransactionDirection>(
                    t.TransactionDate,
                    t.Amount.Amount,
                    t.Direction))
                .ToListAsync(cancellationToken);

            existingCounterLegs[counterId] = [.. rows];
        }

        var toInsert = new List<Transaction>();
        int skipped = 0;
        var batchId = Guid.CreateVersion7();

        foreach (TransactionToImport item in command.Transactions)
        {
            // Pure read against the snapshot. Two PDF rows with identical
            // (date, amount, description) -- e.g. two ATM hits at the same
            // cashpoint on the same day for the same amount -- are both real
            // and must both be persisted. Mutating the set during the loop
            // would treat the second row as a duplicate of the first and
            // silently drop it.
            string signature = DuplicateSignature.Compute(command.AccountId, item.TransactionDate, item.Amount, item.Description);
            bool isDuplicate = existingSignatures.Contains(signature);

            if (!isDuplicate && item.IsTransfer
                && existingTransferLegs.Contains((item.TransactionDate, item.Amount, item.Direction)))
            {
                isDuplicate = true;
            }

            if (isDuplicate)
            {
                skipped++;
                continue;
            }

            // Defensive: imports always denominate amounts in the target
            // account's currency. The maib parser produces MDL into MDL
            // accounts, but other parsers may not.
            var money = new Money(item.Amount, accountCurrency);

            // FX-convert per row at its own transaction date - the domain
            // event consumed by the budget handler needs the MDL value at
            // the row's date, not at "now". For MDL accounts this is the
            // identity case and returns the amount unchanged.
            decimal? amountMdl = await fxConverter.ConvertAsync(
                item.Amount,
                accountCurrency,
                ReportingCurrencies.Mdl,
                item.TransactionDate,
                cancellationToken);

            Result<Transaction> txResult = Transaction.Create(
                command.AccountId,
                item.TransactionDate,
                item.Direction,
                money,
                item.Description,
                TransactionSource.Imported,
                item.CategoryId,
                batchId,
                item.OriginalAmount,
                item.OriginalCurrency,
                item.IsTransfer,
                item.CounterAccountId,
                amountMdl: amountMdl,
                notes: item.Notes);

            if (txResult.IsFailure)
            {
                return Result.Failure<CommitResultDto>(txResult.Error);
            }

            toInsert.Add(txResult.Value);

            if (item.IsTransfer && item.CounterAccountId is { } counterId)
            {
                TransactionDirection oppositeDirection = item.Direction == TransactionDirection.Income
                    ? TransactionDirection.Expense
                    : TransactionDirection.Income;

                // The counter leg is denominated in the counter account's own
                // currency. For cross-currency transfers the caller supplies the
                // destination nominal amount (e.g. MDL row -> 1000 USD on the
                // counter account); same-currency transfers reuse the source amount.
                Account counter = counterAccounts[counterId];
                bool crossCurrency = !string.Equals(
                    counter.Balance.Currency,
                    accountCurrency,
                    StringComparison.Ordinal);

                if (crossCurrency && (item.CounterAmount is null || item.CounterAmount.Value <= 0))
                {
                    return Result.Failure<CommitResultDto>(TransactionErrors.CounterAmountRequired);
                }

                decimal counterAmount = crossCurrency ? item.CounterAmount!.Value : item.Amount;

                // Skip the matching leg only if the counter account ALREADY
                // had a row matching (date, counterAmount, direction) before this
                // import started. Pure read against the snapshot -- never
                // mutate it -- so two batch rows that both warrant a matching
                // leg each get their own leg, matching the source side. Keying on
                // the counter leg's NATIVE amount keeps cross-currency dedup correct.
                if (existingCounterLegs.TryGetValue(counterId, out HashSet<(DateOnly, decimal, TransactionDirection)>? counterLegs)
                    && counterLegs.Contains((item.TransactionDate, counterAmount, oppositeDirection)))
                {
                    continue;
                }

                var counterMoney = new Money(counterAmount, counter.Balance.Currency);

                Result<Transaction> matchingResult = Transaction.Create(
                    counterId,
                    item.TransactionDate,
                    oppositeDirection,
                    counterMoney,
                    item.Description,
                    TransactionSource.Imported,
                    item.CategoryId,
                    batchId,
                    // Cross-currency: stamp the counter leg with the source row's
                    // amount+currency. Same-currency: keep the parser-provided Original*.
                    crossCurrency ? item.Amount : item.OriginalAmount,
                    crossCurrency ? accountCurrency : item.OriginalCurrency,
                    isTransfer: true,
                    counterAccountId: command.AccountId,
                    amountMdl: amountMdl,
                    // Mirror the user's note onto the counter leg too, so the same
                    // annotation is visible from BOTH accounts (e.g. an ATM→Cash
                    // withdrawal explains itself on the Cash side as well).
                    notes: item.Notes);

                if (matchingResult.IsFailure)
                {
                    return Result.Failure<CommitResultDto>(matchingResult.Error);
                }

                toInsert.Add(matchingResult.Value);
            }
        }

        // imported_count reflects user-facing preview rows, not paired-leg multiplicity.
        int importedCount = command.Transactions.Count - skipped;

        Result<ImportBatch> batchResult = ImportBatch.Create(
            command.AccountId,
            command.FileName,
            command.FileHash,
            command.BankSource,
            clock.UtcNow,
            importedCount,
            skipped);

        if (batchResult.IsFailure)
        {
            return Result.Failure<CommitResultDto>(batchResult.Error);
        }

        // Overwrite the generated id so it matches the ImportBatchId stamped on each transaction.
        ImportBatch batch = batchResult.Value;
        SetBatchId(batch, batchId);

        db.ImportBatches.Add(batch);
        foreach (Transaction tx in toInsert)
        {
            db.Transactions.Add(tx);
        }

        // Best-effort pattern learning: keyword->category rules the user opted to
        // remember during the import. Upserted in the SAME unit of work as the
        // transactions, but never allowed to fail the import. These only affect
        // FUTURE imports (the suggester runs at parse time, not here).
        await LearnPatternsAsync(command.LearnedPatterns ?? [], cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new CommitResultDto(batch.Id, importedCount, skipped);
    }

    /// <summary>
    /// Upserts learned keyword->category rules as <c>Learned</c>
    /// <see cref="CategoryPattern"/> rows. Best-effort: blank keywords, keywords
    /// that already exist (as a rule or already added this batch), unknown
    /// categories, and <c>Create</c> failures are all skipped silently. Never
    /// throws and never returns a failure -- the import must succeed regardless.
    /// </summary>
    private async Task LearnPatternsAsync(
        IReadOnlyList<LearnedCategoryPattern> learnedPatterns,
        CancellationToken cancellationToken)
    {
        if (learnedPatterns.Count == 0)
        {
            return;
        }

        // Pre-load existing keywords once (already stored upper-cased) so we
        // don't clobber an existing rule pointing at a different category.
        var existingKeywords = (await db.CategoryPatterns
            .Select(p => p.Keyword)
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        Guid[] candidateCategoryIds = [.. learnedPatterns
            .Select(p => p.CategoryId)
            .Where(id => id != Guid.Empty)
            .Distinct()];

        HashSet<Guid> knownCategoryIds = candidateCategoryIds.Length == 0
            ? []
            : (await db.Categories
                .Where(c => candidateCategoryIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken))
                .ToHashSet();

        foreach (LearnedCategoryPattern learned in learnedPatterns)
        {
            if (string.IsNullOrWhiteSpace(learned.Keyword))
            {
                continue;
            }

            string keyword = learned.Keyword.Trim().ToUpperInvariant();

            // Already a rule (pre-existing or added earlier in this batch) --
            // don't overwrite it.
            if (existingKeywords.Contains(keyword))
            {
                continue;
            }

            if (!knownCategoryIds.Contains(learned.CategoryId))
            {
                continue;
            }

            Result<CategoryPattern> patternResult = CategoryPattern.Create(
                keyword,
                learned.CategoryId,
                CategoryPatternSource.Learned);

            // A Create failure (e.g. keyword too long) is skipped, not fatal.
            if (patternResult.IsFailure)
            {
                continue;
            }

            db.CategoryPatterns.Add(patternResult.Value);
            existingKeywords.Add(keyword);
        }
    }

    private static void SetBatchId(ImportBatch batch, Guid id)
    {
        // ImportBatch.Create allocates a fresh Guid; we need both rows to share the same id.
        typeof(SharedKernel.Entity)
            .GetProperty(nameof(SharedKernel.Entity.Id))!
            .SetValue(batch, id);
    }
}
