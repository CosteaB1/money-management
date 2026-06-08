using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Application.Features.Accounts;

public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    string Currency,
    DateOnly OpeningDate,
    bool IsArchived,
    string? Notes,
    decimal Balance,
    decimal? BalanceMdl);
