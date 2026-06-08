using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Application.Features.Accounts.CreateAccount;

public sealed record CreateAccountCommand(
    string Name,
    AccountType Type,
    decimal Balance,
    string Currency,
    DateOnly OpeningDate,
    string? Notes) : ICommand<Guid>;
