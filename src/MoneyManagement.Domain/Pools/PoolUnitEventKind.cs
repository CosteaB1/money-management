namespace MoneyManagement.Domain.Pools;

/// <summary>
/// What a <see cref="PoolUnitEvent"/> did to a participant's unit balance, and
/// whether real cash moved alongside it.
/// <para>
/// The enum is persisted as its NAME (see <c>PoolUnitEventConfiguration</c>), so
/// members may be added but never renamed or renumbered.
/// </para>
/// </summary>
public enum PoolUnitEventKind
{
    /// <summary>
    /// Bootstrap. The owner's EXISTING account balance simply becomes units at
    /// <c>navPerUnit = 1</c> — no cash enters the account, so a seed carries no
    /// <c>Cash</c> and no movement transaction. Exactly one per pool, owner
    /// only, dated on the pool's inception date.
    /// <para>
    /// Without this carve-out, seeding 1,050 units would synthesize a 1,050
    /// Income leg and double the account.
    /// </para>
    /// </summary>
    Seed = 0,

    /// <summary>Cash in. Mints units at the prevailing NAV, which leaves NAV unchanged.</summary>
    Subscription = 1,

    /// <summary>Cash out. Burns units at the prevailing NAV, which leaves NAV unchanged.</summary>
    Redemption = 2,

    /// <summary>
    /// Profit payout. TWO-PHASE: the month closes (units burn) on
    /// <c>OccurredOn</c>, but the transfer physically leaves later — until it
    /// does, <c>SettledOn</c> and <c>MovementTransactionId</c> are both null and
    /// the cash is still sitting in the account. This is the ONLY kind allowed
    /// to carry cash with a null <c>SettledOn</c>.
    /// </summary>
    Distribution = 3,

    /// <summary>
    /// A participant's share of a pool cost the OWNER paid out of pocket (a
    /// server bill, say). The money never enters the account, so this must not
    /// mint value: it is one half of a PURE UNIT TRANSFER — the participant's
    /// units fall, the owner's rise by the same total, at the same NAV on the
    /// same date. No cash leg, no transaction.
    /// </summary>
    CostShare = 4,

    /// <summary>
    /// The owner side of the transfer described on <see cref="CostShare"/> —
    /// the owner recovering the cost in units. No cash leg, no transaction.
    /// </summary>
    CostRecovery = 5,
}
