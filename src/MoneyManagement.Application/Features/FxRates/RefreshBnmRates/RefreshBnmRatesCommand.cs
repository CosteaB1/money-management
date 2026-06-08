using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.FxRates.RefreshBnmRates;

/// <summary>
/// Pulls BNM rates for <paramref name="Date"/> (defaults to today UTC) and
/// upserts <see cref="MoneyManagement.Domain.FxRates.FxRateSource.BnmAuto"/>
/// rows for every currency the user holds. Manual rates always win — if a
/// <see cref="MoneyManagement.Domain.FxRates.FxRateSource.Manual"/> row
/// exists for the same (from, MDL, asOf) triple, the upstream value is
/// skipped, not overwritten.
/// </summary>
/// <param name="Date">As-of date to fetch; <c>null</c> means today UTC.</param>
/// <param name="CurrencyFilter">
/// Optional whitelist of foreign currencies (3-letter ISO codes, no MDL).
/// When <c>null</c>, the handler derives the set from <c>Accounts.Currency</c>
/// (distinct, excluding MDL).
/// </param>
public sealed record RefreshBnmRatesCommand(
    DateOnly? Date = null,
    IReadOnlyList<string>? CurrencyFilter = null) : ICommand<RefreshBnmRatesResponse>;

/// <summary>
/// Counts surfaced to API callers and to <c>BnmAutoFetchService</c> logs.
/// </summary>
/// <param name="Fetched">Total rates returned by BNM (before filtering).</param>
/// <param name="Inserted">Rows newly inserted as BnmAuto.</param>
/// <param name="Updated">Existing BnmAuto rows that had their value refreshed.</param>
/// <param name="Skipped">Either: Manual rate exists, or BnmAuto already matches, or currency not held.</param>
public sealed record RefreshBnmRatesResponse(int Fetched, int Inserted, int Updated, int Skipped);
