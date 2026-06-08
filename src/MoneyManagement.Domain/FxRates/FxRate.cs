using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.FxRates;

/// <summary>
/// Reference-data exchange rate. <c>Rate</c> means: 1 unit of
/// <see cref="FromCurrency"/> equals <c>Rate</c> units of
/// <see cref="ToCurrency"/> as of <see cref="AsOf"/>.
/// e.g. <c>USD -> MDL = 17.50</c> means 1 USD buys 17.50 MDL.
/// </summary>
public sealed class FxRate : Entity
{
    public const int CurrencyLength = CurrencyCodes.Length;

    // EF Core
    private FxRate() { }

    private FxRate(
        Guid id,
        string fromCurrency,
        string toCurrency,
        decimal rate,
        DateOnly asOf,
        FxRateSource source) : base(id)
    {
        FromCurrency = fromCurrency;
        ToCurrency = toCurrency;
        Rate = rate;
        AsOf = asOf;
        Source = source;
    }

    public string FromCurrency { get; private set; } = string.Empty;
    public string ToCurrency { get; private set; } = string.Empty;
    public decimal Rate { get; private set; }
    public DateOnly AsOf { get; private set; }

    /// <summary>
    /// Whether this row was hand-entered (<see cref="FxRateSource.Manual"/>)
    /// or pulled from BNM (<see cref="FxRateSource.BnmAuto"/>). Manual wins
    /// on collision — <see cref="MoneyManagement.Application.Abstractions.FxRates.IFxConverter"/>
    /// orders by source so Manual is preferred for the same (from, to, asOf) triple.
    /// </summary>
    public FxRateSource Source { get; private set; }

    public static Result<FxRate> Create(
        string fromCurrency,
        string toCurrency,
        decimal rate,
        DateOnly asOf,
        FxRateSource source = FxRateSource.Manual)
    {
        if (!CurrencyCodes.IsValidIso(fromCurrency) || !CurrencyCodes.IsValidIso(toCurrency))
        {
            return Result.Failure<FxRate>(FxRateErrors.InvalidCurrency);
        }

        if (string.Equals(fromCurrency, toCurrency, StringComparison.Ordinal))
        {
            return Result.Failure<FxRate>(FxRateErrors.SameSourceAndTargetCurrency);
        }

        if (rate <= 0m)
        {
            return Result.Failure<FxRate>(FxRateErrors.RateMustBePositive);
        }

        return new FxRate(Guid.CreateVersion7(), fromCurrency, toCurrency, rate, asOf, source);
    }

    /// <summary>
    /// Updates the <see cref="Rate"/> in-place. Used by the BNM auto-fetch
    /// handler to refresh a BnmAuto row when the upstream value changes.
    /// Manual rates are immutable from the application's perspective —
    /// the user deletes and recreates instead.
    /// </summary>
    public Result UpdateRate(decimal rate)
    {
        if (rate <= 0m)
        {
            return Result.Failure(FxRateErrors.RateMustBePositive);
        }

        Rate = rate;
        return Result.Success();
    }
}
