using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Accounts;

public sealed record AccountCreatedDomainEvent(Guid AccountId) : IDomainEvent;
