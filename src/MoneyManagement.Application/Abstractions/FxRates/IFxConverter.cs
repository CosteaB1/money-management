namespace MoneyManagement.Application.Abstractions.FxRates;

/// <summary>
/// Read-side service that converts an <paramref name="amount"/> from one
/// currency to another using the latest <see cref="MoneyManagement.Domain.FxRates.FxRate"/>
/// on or before <paramref name="asOf"/>.
/// </summary>
/// <remarks>
/// Returns <c>null</c> when no usable rate exists - callers MUST treat that
/// case explicitly. There is no implicit 1:1 fallback. The identity case
/// (<c>from == to</c>) short-circuits to <paramref name="amount"/>.
/// </remarks>
public interface IFxConverter
{
    Task<decimal?> ConvertAsync(
        decimal amount,
        string fromCurrency,
        string toCurrency,
        DateOnly asOf,
        CancellationToken cancellationToken);
}
