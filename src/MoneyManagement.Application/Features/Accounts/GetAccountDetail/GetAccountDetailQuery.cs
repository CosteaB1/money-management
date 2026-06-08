using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.Accounts.GetAccountDetail;

public sealed record GetAccountDetailQuery(Guid Id) : IQuery<AccountDetailDto>;
