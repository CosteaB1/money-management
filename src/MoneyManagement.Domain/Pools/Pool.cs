using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Pools;

/// <summary>
/// Outside capital sharing ONE account. Two friends put USD 1,000 each into the
/// user's Binance account; everyone — the user included — holds
/// <see cref="PoolUnitEvent">units</see>, and
/// <c>navPerUnit = poolValue ÷ unitsOutstanding</c>. Contributions mint units at
/// the prevailing NAV and withdrawals burn them, which leaves NAV
/// mathematically unchanged.
/// <para>
/// <b><see cref="AccountId"/> is explicit, immutable and unique.</b> It is never
/// inferred from the account's type: Bybit is also CryptoExchange/USD with an
/// anchor of 0.00, and guessing would silently pool an account nobody else has
/// money in. One pool per account, forever — a second pool would make "the
/// owner's fraction of this account" ambiguous.
/// </para>
/// <para>
/// <b><see cref="Currency"/> must equal the account's.</b> The pool never does
/// FX; NAV is pure native. Any conversion happens later, at the net-worth
/// reader, at its own as-of date.
/// </para>
/// <para>
/// Only <see cref="Name"/> and <see cref="Notes"/> are user-editable — the
/// <c>Loan.Update</c> boundary. Account, currency and inception date are frozen
/// at creation because the entire unit history is denominated against them.
/// </para>
/// </summary>
public sealed class Pool : Entity
{
    public const int NameMaxLength = 100;
    public const int NotesMaxLength = 500;

    // EF Core
    private Pool()
    {
        Name = string.Empty;
        Currency = ReportingCurrencies.Mdl;
    }

    private Pool(
        Guid id,
        Guid accountId,
        string name,
        string currency,
        DateOnly inceptionDate,
        string? notes) : base(id)
    {
        AccountId = accountId;
        Name = name;
        Currency = currency;
        InceptionDate = inceptionDate;
        Notes = notes;
        IsArchived = false;
    }

    public Guid AccountId { get; private set; }
    public string Name { get; private set; }
    public string Currency { get; private set; }
    public DateOnly InceptionDate { get; private set; }
    public string? Notes { get; private set; }
    public bool IsArchived { get; private set; }

    /// <param name="accountId">The account the pool's capital physically sits in.</param>
    /// <param name="name">Display name, e.g. "Binance pool".</param>
    /// <param name="currency">The pool's denomination. Must equal <paramref name="accountCurrency"/>.</param>
    /// <param name="accountCurrency">
    /// The account's own currency, passed in by the handler — the domain layer
    /// has no way to load the account, so the caller supplies the parent's
    /// invariant exactly as <c>LoanPayment.Create</c> takes <c>loanCurrency</c>.
    /// </param>
    /// <param name="inceptionDate">The day the pool starts. The seed event is dated here.</param>
    /// <param name="notes">Free text, optional.</param>
    /// <param name="clock">Injected so the "no future dates" guard is deterministic in tests.</param>
    public static Result<Pool> Create(
        Guid accountId,
        string name,
        string currency,
        string accountCurrency,
        DateOnly inceptionDate,
        string? notes,
        IDateTimeProvider clock)
    {
        if (accountId == Guid.Empty)
        {
            return Result.Failure<Pool>(PoolErrors.AccountRequired);
        }

        Result<string> nameValidation = ValidateName(name);
        if (nameValidation.IsFailure)
        {
            return Result.Failure<Pool>(nameValidation.Error);
        }

        if (!CurrencyCodes.IsValidIso(currency))
        {
            return Result.Failure<Pool>(PoolErrors.InvalidCurrency);
        }

        // The pool never converts. If it could be denominated differently from
        // the account it sits in, every NAV would need an FX rate and the
        // "units × nav == account value" identity would drift with the market.
        if (!string.Equals(currency, accountCurrency, StringComparison.Ordinal))
        {
            return Result.Failure<Pool>(PoolErrors.CurrencyMismatch);
        }

        // No future-dated pools. Judged in UTC via the injected clock — same
        // convention as Loan.Create's loan-date guard.
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (inceptionDate > today)
        {
            return Result.Failure<Pool>(PoolErrors.InceptionDateInFuture);
        }

        Result<string?> notesValidation = ValidateNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure<Pool>(notesValidation.Error);
        }

        return new Pool(
            Guid.CreateVersion7(),
            accountId,
            nameValidation.Value,
            currency,
            inceptionDate,
            notesValidation.Value);
    }

    /// <summary>
    /// Edits the user-mutable metadata. Name and notes only — the account,
    /// currency and inception date are immutable after creation (every unit
    /// event is denominated and dated against them).
    /// </summary>
    public Result Update(string name, string? notes)
    {
        Result<string> nameValidation = ValidateName(name);
        if (nameValidation.IsFailure)
        {
            return Result.Failure(nameValidation.Error);
        }

        Result<string?> notesValidation = ValidateNotes(notes);
        if (notesValidation.IsFailure)
        {
            return Result.Failure(notesValidation.Error);
        }

        Name = nameValidation.Value;
        Notes = notesValidation.Value;
        return Result.Success();
    }

    /// <summary>
    /// Idempotent: archiving an already-archived pool is a no-op.
    /// <para>
    /// <paramref name="outsideUnitsOutstanding"/> is the total units held by
    /// NON-owner participants, computed by the caller (the domain layer cannot
    /// query). It must be zero: archiving hides the pool from the default
    /// queries, which reverts the account's owner fraction to 1.0 — with a
    /// friend's stake still in there, that silently reabsorbs their money into
    /// the user's net worth.
    /// </para>
    /// </summary>
    public Result Archive(decimal outsideUnitsOutstanding)
    {
        if (Math.Abs(outsideUnitsOutstanding) > PoolUnitEvent.UnitsDustTolerance)
        {
            return Result.Failure(PoolErrors.PoolHasOutsideUnits);
        }

        IsArchived = true;
        return Result.Success();
    }

    /// <summary>Idempotent: unarchiving an already-active pool is a no-op.</summary>
    public Result Unarchive()
    {
        IsArchived = false;
        return Result.Success();
    }

    private static Result<string> ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<string>(PoolErrors.NameRequired);
        }

        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            return Result.Failure<string>(PoolErrors.NameTooLong);
        }

        return Result.Success(trimmed);
    }

    private static Result<string?> ValidateNotes(string? notes)
    {
        string? trimmed = notes?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Result.Success<string?>(null);
        }

        if (trimmed.Length > NotesMaxLength)
        {
            return Result.Failure<string?>(PoolErrors.NotesTooLong);
        }

        return Result.Success<string?>(trimmed);
    }
}
