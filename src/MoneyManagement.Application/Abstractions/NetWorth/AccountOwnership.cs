namespace MoneyManagement.Application.Abstractions.NetWorth;

/// <summary>
/// How much of one account's balance actually belongs to the user, as a dated
/// step function.
/// <para>
/// This is the counterpart to <see cref="ExternalClaim"/> for outside money the
/// user holds but does not own. A claim models a DECLINING obligation
/// (<c>principal − Σ settlements</c>) and is subtracted after the fact; that
/// shape cannot express pooled capital, where a friend's stake is
/// <c>units × navPerUnit</c> and therefore RISES when the pool rises. Rather
/// than subtract a moving target back out of a number that already counted it,
/// this seam says the outside money was never the user's asset in the first
/// place: the balance is multiplied by the owner's fraction at read time.
/// </para>
/// <para>
/// Nothing here knows about units, NAV or profit shares — that arithmetic
/// belongs to the producer, which collapses it into a plain fraction per date.
/// </para>
/// </summary>
/// <param name="AccountId">The account whose balance the fractions apply to.</param>
/// <param name="Points">
/// The fraction's change history, <b>oldest first, at most one point per
/// distinct date</b>. This ordering is the PRODUCER's contract, not something
/// this type re-establishes: <see cref="OwnedFractionAsOf"/> stops at the first
/// point past the as-of date, so an unsorted list silently yields the wrong
/// answer. Consumers deliberately do not sort defensively — a producer that
/// emits garbage should be fixed, not papered over on every one of the 24
/// as-of evaluations a single trend request performs.
/// </param>
public sealed record AccountOwnership(Guid AccountId, IReadOnlyList<OwnedFractionPoint> Points)
{
    /// <summary>
    /// The share of the account's balance the user owns on
    /// <paramref name="asOf"/> — the fraction carried by the newest point dated
    /// on or before that day.
    /// <list type="bullet">
    /// <item>
    /// <b>1.0 before the first point</b> (and when there are no points at all):
    /// an account nobody else has put money into is wholly the user's. The
    /// absence of ownership data is never read as "owns nothing".
    /// </item>
    /// <item>
    /// <b>END-OF-DAY cutoff</b> — a point dated exactly <paramref name="asOf"/>
    /// APPLIES, matching <c>AccountBalanceLedger.NativeBalanceAsOf</c>'s
    /// <c>movement.Date &lt;= asOf</c> rule. The value and the fraction MUST
    /// share one cutoff: a subscription that lands on a month-end raises the
    /// balance on that day, so if the dilution only took effect the next day the
    /// owner's share would be inflated for exactly one trend point — a phantom
    /// spike on the chart that reverses itself a month later.
    /// </item>
    /// <item>
    /// The last matching point wins; every earlier one is superseded.
    /// </item>
    /// </list>
    /// </summary>
    public decimal OwnedFractionAsOf(DateOnly asOf)
    {
        decimal fraction = 1m;

        foreach (OwnedFractionPoint p in Points)
        {
            if (p.EffectiveFrom > asOf)
            {
                // Points are oldest-first, so the first future point means every
                // remaining one is future too.
                break;
            }

            fraction = p.Fraction;
        }

        return fraction;
    }
}

/// <summary>
/// One step of the ownership curve: from <paramref name="EffectiveFrom"/>
/// onwards (inclusive — see <see cref="AccountOwnership.OwnedFractionAsOf"/>),
/// the user owns <paramref name="Fraction"/> of the account.
/// </summary>
/// <param name="EffectiveFrom">The day the fraction starts applying, inclusive.</param>
/// <param name="Fraction">
/// The user's share, in <c>[0, 1]</c>: <c>1</c> = wholly the user's, <c>0</c> =
/// entirely outside capital. Producers guarantee the range; this record does not
/// clamp, so an out-of-range value surfaces as an obviously wrong dashboard
/// number instead of being silently rounded into plausibility.
/// </param>
public sealed record OwnedFractionPoint(DateOnly EffectiveFrom, decimal Fraction);
