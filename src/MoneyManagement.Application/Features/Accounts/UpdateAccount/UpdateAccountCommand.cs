using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Accounts.UpdateAccount;

public sealed record UpdateAccountCommand(Guid Id, string Name, string? Notes) : ICommand;
