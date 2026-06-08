using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.FxRates.ConvertFx;

/// <summary>
/// Read-only FX conversion used by the cross-currency transfer / import UIs to
/// pre-fill the editable destination (counter) amount. Wraps
/// <see cref="MoneyManagement.Application.Abstractions.FxRates.IFxConverter"/>
/// and surfaces the derived effective rate alongside the converted value.
/// </summary>
public sealed record ConvertFxQuery(
    string From,
    string To,
    DateOnly Date,
    decimal Amount) : IQuery<ConvertFxResult>;

/// <summary>
/// <paramref name="ConvertedAmount"/> and <paramref name="Rate"/> are null when
/// no usable FX rate exists at <see cref="ConvertFxQuery.Date"/>
/// (<paramref name="HasRate"/> = false). The effective rate is derived as
/// converted/amount and is never persisted.
/// </summary>
public sealed record ConvertFxResult(decimal? ConvertedAmount, decimal? Rate, bool HasRate);
