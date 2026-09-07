using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Application.Features.Accounts;

/// <summary>
/// One row of <c>/accounts</c>.
/// <para>
/// <c>Balance</c> is the account's FULL value, pooled or not — the account
/// really does hold the outside participants' money, and this surface was
/// decided explicitly to keep showing all of it. <c>IsPooled</c> is the flag
/// the list badges off, and it is the ONLY thing a pool changes here: no
/// fraction is applied, exactly as on Balance-over-time and <c>GetSummary</c>.
/// Net worth and the account-detail Performance card are the two surfaces that
/// take the owner's share instead.
/// </para>
/// <para>
/// <c>IsPooled</c> means a NON-ARCHIVED pool sits on the account — the
/// definition owned by <c>PooledAccountGuard</c>, which produces this flag.
/// </para>
/// </summary>
public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    string Currency,
    DateOnly OpeningDate,
    bool IsArchived,
    bool IsPooled,
    string? Notes,
    decimal Balance,
    decimal? BalanceMdl);
