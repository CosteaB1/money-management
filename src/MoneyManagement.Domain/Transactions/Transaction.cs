using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Transactions;

public sealed class Transaction : Entity
{
    public const int DescriptionMaxLength = 500;
    public const int NotesMaxLength = 500;

    // EF Core
    private Transaction()
    {
        // EF Core overwrites this on materialization; the literal here exists
        // only to satisfy non-nullable construction. Phase 4 widened the
        // accepted currency set, so any ISO code on the row will round-trip.
        Amount = Money.Zero(ReportingCurrencies.Mdl);
    }

    private Transaction(
        Guid id,
        Guid accountId,
        DateOnly transactionDate,
        TransactionDirection direction,
        Money amount,
        string description,
        TransactionSource source,
        Guid? categoryId,
        Guid? importBatchId,
        decimal? originalAmount,
        string? originalCurrency,
        bool isTransfer,
        Guid? counterAccountId,
        bool isAdjustment,
        string? notes) : base(id)
    {
        AccountId = accountId;
        TransactionDate = transactionDate;
        Direction = direction;
        Amount = amount;
        Description = description;
        Source = source;
        CategoryId = categoryId;
        ImportBatchId = importBatchId;
        OriginalAmount = originalAmount;
        OriginalCurrency = originalCurrency;
        IsTransfer = isTransfer;
        CounterAccountId = counterAccountId;
        IsAdjustment = isAdjustment;
        Notes = notes;
        IsDeleted = false;
    }

    public Guid AccountId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public TransactionDirection Direction { get; private set; }
    public Money Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal? OriginalAmount { get; private set; }
    public string? OriginalCurrency { get; private set; }
    public TransactionSource Source { get; private set; }
    public Guid? ImportBatchId { get; private set; }
    public bool IsTransfer { get; private set; }
    public Guid? CounterAccountId { get; private set; }
    public bool IsAdjustment { get; private set; }
    public string? Notes { get; private set; }
    public bool IsDeleted { get; private set; }

    public static Result<Transaction> Create(
        Guid accountId,
        DateOnly transactionDate,
        TransactionDirection direction,
        Money amount,
        string description,
        TransactionSource source,
        Guid? categoryId = null,
        Guid? importBatchId = null,
        decimal? originalAmount = null,
        string? originalCurrency = null,
        bool isTransfer = false,
        Guid? counterAccountId = null,
        bool isAdjustment = false,
        decimal? amountMdl = null,
        string? notes = null)
    {
        if (accountId == Guid.Empty)
        {
            return Result.Failure<Transaction>(TransactionErrors.AccountRequired);
        }

        if (!Enum.IsDefined(direction))
        {
            return Result.Failure<Transaction>(TransactionErrors.InvalidDirection);
        }

        if (!Enum.IsDefined(source))
        {
            return Result.Failure<Transaction>(TransactionErrors.InvalidSource);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure<Transaction>(TransactionErrors.DescriptionRequired);
        }

        if (description.Length > DescriptionMaxLength)
        {
            return Result.Failure<Transaction>(TransactionErrors.DescriptionTooLong);
        }

        if (notes is { Length: > NotesMaxLength })
        {
            return Result.Failure<Transaction>(TransactionErrors.NotesTooLong);
        }

        if (amount.Amount <= 0)
        {
            return Result.Failure<Transaction>(TransactionErrors.AmountNotPositive);
        }

        if (!CurrencyCodes.IsValidIso(amount.Currency))
        {
            return Result.Failure<Transaction>(TransactionErrors.InvalidCurrency);
        }

        // No future-dated transactions. The rule is judged in UTC; the client
        // also submits/validates dates in UTC, so the two agree regardless of
        // the user's location.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (transactionDate > today)
        {
            return Result.Failure<Transaction>(TransactionErrors.DateInFuture);
        }

        if (originalCurrency is not null && originalCurrency.Length != 3)
        {
            return Result.Failure<Transaction>(TransactionErrors.InvalidOriginalCurrency);
        }

        if (originalAmount is { } oa && oa <= 0)
        {
            return Result.Failure<Transaction>(TransactionErrors.OriginalAmountNotPositive);
        }

        if (counterAccountId is not null && !isTransfer)
        {
            return Result.Failure<Transaction>(TransactionErrors.CounterAccountWithoutTransferFlag);
        }

        if (counterAccountId is { } counterId && counterId == accountId)
        {
            return Result.Failure<Transaction>(TransactionErrors.CounterAccountCannotBeSelf);
        }

        if (isTransfer && isAdjustment)
        {
            return Result.Failure<Transaction>(TransactionErrors.TransferAndAdjustmentAreMutuallyExclusive);
        }

        var transaction = new Transaction(
            Guid.CreateVersion7(),
            accountId,
            transactionDate,
            direction,
            amount,
            description.Trim(),
            source,
            categoryId,
            importBatchId,
            originalAmount,
            originalCurrency,
            isTransfer,
            counterAccountId,
            isAdjustment,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());

        // The MDL-equivalent is supplied by the calling handler (which has the
        // IFxConverter dependency). It's nullable because no usable rate at the
        // transaction date is a legitimate state — downstream handlers must
        // tolerate it (see UpdateBudgetPeriodOnTransactionCreatedHandler).
        transaction.Raise(new TransactionCreatedDomainEvent(
            transaction.Id,
            transaction.CategoryId,
            transaction.TransactionDate,
            amountMdl,
            transaction.Direction,
            transaction.IsTransfer,
            transaction.IsAdjustment));

        return transaction;
    }

    /// <summary>
    /// Soft-deletes the transaction. Idempotent — already-deleted rows are a
    /// no-op. Raises <see cref="TransactionDeletedDomainEvent"/> so the budget
    /// handler can subtract this row's spend from the matching period.
    /// </summary>
    /// <param name="amountMdl">
    /// FX-converted MDL value at <see cref="TransactionDate"/>, supplied by
    /// the calling handler (which has the <c>IFxConverter</c> dependency).
    /// Nullable for the same reason as the create event: when no usable rate
    /// is available, downstream handlers must skip. Defaults to <c>null</c>
    /// for test sites that don't care about budget side-effects.
    /// </param>
    public void MarkDeleted(decimal? amountMdl = null)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        Raise(new TransactionDeletedDomainEvent(
            Id,
            CategoryId,
            TransactionDate,
            amountMdl,
            Direction,
            IsTransfer,
            IsAdjustment));
    }

    /// <summary>
    /// Reassigns the owning category; <c>null</c> clears (uncategorizes).
    /// Idempotent — calls that don't change the value are a no-op and do not
    /// raise an event. Otherwise raises
    /// <see cref="TransactionCategoryChangedDomainEvent"/> carrying both the
    /// old and the new category id so the budget handler can move spend in a
    /// single pass.
    /// </summary>
    /// <param name="amountMdl">
    /// FX-converted MDL value at <see cref="TransactionDate"/>; same contract
    /// as <see cref="MarkDeleted"/>'s parameter.
    /// </param>
    public void SetCategory(Guid? categoryId, decimal? amountMdl = null)
    {
        if (CategoryId == categoryId)
        {
            return;
        }

        Guid? oldCategoryId = CategoryId;
        CategoryId = categoryId;
        Raise(new TransactionCategoryChangedDomainEvent(
            Id,
            oldCategoryId,
            categoryId,
            TransactionDate,
            amountMdl,
            Direction,
            IsTransfer,
            IsAdjustment));
    }

    /// <summary>
    /// Sets the free-text user annotation; blank/whitespace normalizes to
    /// <c>null</c>. Idempotent — a call that doesn't change the stored value is a
    /// no-op. Notes carry no budget/report/balance meaning, so this raises no
    /// domain event. Length is gated by the command validator, not re-validated
    /// here.
    /// </summary>
    public void SetNotes(string? notes)
    {
        string? normalized = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (Notes == normalized)
        {
            return;
        }

        Notes = normalized;
    }
}
