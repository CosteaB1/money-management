using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Accounts.ArchiveAccount;

public sealed record ArchiveAccountCommand(Guid Id) : ICommand;
