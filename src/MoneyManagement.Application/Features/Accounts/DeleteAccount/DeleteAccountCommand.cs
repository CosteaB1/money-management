using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Accounts.DeleteAccount;

public sealed record DeleteAccountCommand(Guid Id) : ICommand;
