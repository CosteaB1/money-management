using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Accounts.UnarchiveAccount;

public sealed record UnarchiveAccountCommand(Guid Id) : ICommand;
