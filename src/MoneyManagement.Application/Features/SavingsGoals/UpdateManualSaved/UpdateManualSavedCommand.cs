using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.SavingsGoals.UpdateManualSaved;

public sealed record UpdateManualSavedCommand(Guid Id, decimal Amount) : ICommand;
