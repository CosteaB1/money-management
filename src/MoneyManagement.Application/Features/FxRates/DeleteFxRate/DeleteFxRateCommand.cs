using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.FxRates.DeleteFxRate;

public sealed record DeleteFxRateCommand(Guid Id) : ICommand;
