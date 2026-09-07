using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Application.Features.Accounts;

/// <summary>
/// The <c>/accounts/{id}</c> projection.
/// <para>
/// <c>Balance</c> / <c>BalanceMdl</c> are the account's FULL value even when it
/// is pooled — the account really does hold the outside participants' money,
/// and the read-side divergence was decided explicitly: <c>/accounts</c>,
/// Balance-over-time and <c>GetSummary</c> all show the whole balance with a
/// "Pooled" badge, while the net-worth card, the net-worth trend and
/// <see cref="AllTime"/> / <see cref="YearToDate"/> below apply the owner's
/// share. <see cref="IsPooled"/> is what lets the UI say which of the two a
/// given number is.
/// </para>
/// <para>
/// <c>IsPooled</c> means a NON-ARCHIVED pool sits on the account — the same
/// definition every write guard uses (<c>PooledAccountGuard.IsPooledAsync</c>),
/// because archiving a pool already requires zero outside units, so an archived
/// pool's account is a wholly-owned account again.
/// </para>
/// </summary>
public sealed record AccountDetailDto(
    Guid Id,
    string Name,
    AccountType Type,
    string Currency,
    DateOnly OpeningDate,
    bool IsArchived,
    bool IsPooled,
    string? Notes,
    decimal Balance,
    decimal? BalanceMdl,
    decimal InitialCapital,
    AccountActivityTotalsDto AllTime,
    AccountActivityTotalsDto YearToDate,
    DateOnly? FirstActivityDate,
    DateOnly? LastActivityDate,
    int RealActivityCount);

/// <summary>
/// The Performance card's three figures for one window, plus the row counts
/// behind them.
/// <para>
/// <b>All three are the OWNER's, not the account's</b>, on a pooled account:
/// another participant's subscription is not the user's contribution, their
/// payout is not the user's withdrawal, and only the user's share of a
/// re-pricing mark is the user's profit. On an account nobody else has money in
/// — every account, today — the owner's share is 1.0 and these are exactly the
/// account's own figures. See <c>GetAccountDetailQueryHandler</c> for the two
/// (deliberately different) rules that produce them.
/// </para>
/// <para>
/// The COUNTS follow the money they explain: a leg excluded as somebody else's
/// is not counted either, or the card would read "2 contributions totalling
/// 842.89" for one contribution. <see cref="AdjustmentCount"/> is the
/// exception and stays account-level — a mark is one event on the account
/// however it is split.
/// </para>
/// </summary>
public sealed record AccountActivityTotalsDto(
    decimal ContributionsMdl,
    decimal WithdrawalsMdl,
    decimal NetPnLMdl,
    int ContributionCount,
    int WithdrawalCount,
    int AdjustmentCount,
    bool MissingFxRate);
