using MoneyManagement.Domain.FxRates;

namespace MoneyManagement.Application.Features.FxRates;

public sealed record FxRateDto(
    Guid Id,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateOnly AsOf,
    FxRateSource Source,
    DateTime CreatedAt,
    DateTime UpdatedAt);
