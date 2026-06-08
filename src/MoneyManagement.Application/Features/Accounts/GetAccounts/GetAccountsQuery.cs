using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Accounts.GetAccounts;

public sealed record GetAccountsQuery(bool IncludeArchived = false)
    : IQuery<IReadOnlyList<AccountDto>>;
