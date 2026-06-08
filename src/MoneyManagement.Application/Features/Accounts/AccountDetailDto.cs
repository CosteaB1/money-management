using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Application.Features.Accounts;

public sealed record AccountDetailDto(
    Guid Id,
    string Name,
    AccountType Type,
    string Currency,
    DateOnly OpeningDate,
    bool IsArchived,
    string? Notes,
    decimal Balance,
    decimal? BalanceMdl,
    decimal InitialCapital,
    AccountActivityTotalsDto AllTime,
    AccountActivityTotalsDto YearToDate,
    DateOnly? FirstActivityDate,
    DateOnly? LastActivityDate,
    int RealActivityCount);

public sealed record AccountActivityTotalsDto(
    decimal ContributionsMdl,
    decimal WithdrawalsMdl,
    decimal NetPnLMdl,
    int ContributionCount,
    int WithdrawalCount,
    int AdjustmentCount,
    bool MissingFxRate);
