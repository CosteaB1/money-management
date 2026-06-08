using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.FxRates.CreateFxRate;

public sealed record CreateFxRateCommand(
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    DateOnly AsOf) : ICommand<Guid>;
