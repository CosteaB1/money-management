namespace MoneyManagement.Application.Features.Pools;

/// <summary>
/// Read-side projection over a <c>Pool</c>, for the <c>/pools</c> list.
/// <para>
/// Every native monetary field is in the pool's own <see cref="Currency"/>
/// (which always equals the account's — the pool never does FX).
/// <see cref="PoolValueMdl"/> and <see cref="OutsideCapitalMdl"/> are the
/// reporting-currency conversion at TODAY's rate, <c>null</c> with
/// <see cref="MissingFxRate"/> flipped when no usable rate exists. Same contract
/// as <c>AccountDto.BalanceMdl</c>: never a silent zero, never an implicit 1:1.
/// </para>
/// </summary>
/// <param name="Id">The pool.</param>
/// <param name="AccountId">The account the capital physically sits in.</param>
/// <param name="AccountName">That account's name; an archived account still labels its pool.</param>
/// <param name="Name">Display name.</param>
/// <param name="Currency">The pool's denomination.</param>
/// <param name="InceptionDate">The day the pool started; the seed is dated here.</param>
/// <param name="Notes">Free text.</param>
/// <param name="IsArchived">Archived pools are hidden unless the caller opts in.</param>
/// <param name="AccountBalance">
/// The account's DERIVED balance today (anchor + Σ income − Σ expense), not the
/// stored anchor. Surfaced because it is the only thing that explains a
/// <see cref="PoolValue"/> that differs from what the accounts page shows.
/// </param>
/// <param name="UnpaidDistributionCash">
/// Closed-but-unpaid distributions still sitting in the account. Subtracted from
/// <see cref="AccountBalance"/> to get <see cref="PoolValue"/> — skip that and
/// the money counts as pool value a second time and the friends are paid twice
/// on the same profit.
/// </param>
/// <param name="UnpaidDistributionCount">How many distribution events are still owed.</param>
/// <param name="PoolValue"><c>AccountBalance − UnpaidDistributionCash</c>. What units are priced against.</param>
/// <param name="PoolValueMdl">Reporting-currency value of <see cref="PoolValue"/> at today's rate.</param>
/// <param name="TotalUnits">Units outstanding across every participant, archived included.</param>
/// <param name="NavPerUnit">
/// <c>PoolValue ÷ TotalUnits</c>, or <c>null</c> when no units are outstanding.
/// Never substituted with a placeholder — no units means no price.
/// </param>
/// <param name="ParticipantCount">Non-archived participants, the owner included.</param>
/// <param name="OwnerFraction">
/// The user's share of the POOL, in <c>[0, 1]</c>: <c>ownerUnits ÷ totalUnits</c>
/// with units already retired at the close of any distribution.
/// <para>
/// <b>While a payout is closed but unpaid this is NOT the same number the
/// net-worth seam multiplies by, and deliberately so.</b> This fraction is
/// applied to <see cref="PoolValue"/>, which has the owed cash netted out; the
/// dashboard applies its own to the raw <see cref="AccountBalance"/>, so it
/// keeps those units outstanding until the transfer actually goes out. The two
/// bases differ by exactly the owed cash and the two fractions differ by exactly
/// the same ratio, so <b>the VALUES agree to the cent at all times</b>:
/// <c>AccountBalance × dashboardFraction == PoolValue × OwnerFraction</c>.
/// Outside a close/settle gap the two fractions are identical.
/// </para>
/// <para>
/// The one case where even the values part company is a payout closed to the
/// OWNER — the dashboard still counts that cash as theirs until it leaves the
/// account, which is correct, and which the default monthly close never
/// produces (it excludes the owner on purpose).
/// </para>
/// </param>
/// <param name="OutsideCapital">
/// Σ non-owner stakes: other people's money, as a POSITIVE magnitude in the
/// pool's currency.
/// <para>
/// <b>The dashboard's <c>OutsideCapitalMdl</c> reads HIGHER than this while a
/// distribution is closed but unpaid</b> — by exactly the cash owed to
/// non-owners. Both are right, and neither is a rounding artefact: this figure
/// is Σ non-owner STAKES priced against <see cref="PoolValue"/>, while the
/// dashboard reports everything on the account that is not the user's, and a
/// struck-but-untransferred payout is exactly that. They converge the moment it
/// settles.
/// </para>
/// <para>
/// The OWNER's side of the same split does not diverge at all — see
/// <see cref="OwnerFraction"/>.
/// </para>
/// </param>
/// <param name="OutsideCapitalMdl">Reporting-currency value of <see cref="OutsideCapital"/> at today's rate.</param>
/// <param name="MissingFxRate">True when any MDL field above could not be valued.</param>
public sealed record PoolDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string Name,
    string Currency,
    DateOnly InceptionDate,
    string? Notes,
    bool IsArchived,
    decimal AccountBalance,
    decimal UnpaidDistributionCash,
    int UnpaidDistributionCount,
    decimal PoolValue,
    decimal? PoolValueMdl,
    decimal TotalUnits,
    decimal? NavPerUnit,
    int ParticipantCount,
    decimal OwnerFraction,
    decimal OutsideCapital,
    decimal? OutsideCapitalMdl,
    bool MissingFxRate);
