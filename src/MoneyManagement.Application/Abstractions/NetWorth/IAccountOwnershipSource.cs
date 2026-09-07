namespace MoneyManagement.Application.Abstractions.NetWorth;

/// <summary>
/// Supplies the per-account <see cref="AccountOwnership"/> curves that scale the
/// balances net worth is built from. Pooled capital (friends who put money into
/// an account the user holds) is the intended first producer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a SEPARATE seam from <see cref="IExternalClaimSource"/>, and
/// must not be "unified" with it.</b> The two model different economics and
/// apply at different points in the calculation:
/// </para>
/// <list type="bullet">
/// <item>
/// A claim is a <b>declining obligation</b> — <c>principal − Σ settlements</c>,
/// monotonically non-increasing between settlements, denominated in its own
/// currency, subtracted from a total that already counted the money as the
/// user's.
/// </item>
/// <item>
/// An ownership fraction is a <b>proportional interest</b> — a pooled stake is
/// <c>units × navPerUnit</c>, so it RISES when the account rises. Expressing
/// that as a claim would require restating the "principal" on every price move.
/// Instead the outside share is never counted as the user's asset: it is
/// multiplied out of the balance before any claim arithmetic happens.
/// </item>
/// </list>
/// <para>
/// Folding ownership into the claim seam would therefore mean either recomputing
/// a synthetic principal per as-of date or double-counting the outside money
/// (once excluded, once subtracted). Keep them apart.
/// </para>
/// </remarks>
public interface IAccountOwnershipSource
{
    /// <summary>
    /// Every ownership curve that has EVER applied, in full, in one round-trip.
    /// <para>
    /// Intentionally not an "as of date D" query, for the same reason as
    /// <see cref="IExternalClaimSource.GetHistoryAsync"/>: the net-worth trend
    /// evaluates up to 24 as-of dates per request, so a per-point query would
    /// multiply the round-trips by 24. Callers materialize this once (see
    /// <c>AccountOwnershipLedger</c>) and slice it in memory via
    /// <see cref="AccountOwnership.OwnedFractionAsOf"/>.
    /// </para>
    /// <para>
    /// Each returned curve's <see cref="AccountOwnership.Points"/> must be
    /// oldest-first with one point per distinct date — the producer's contract,
    /// which the in-memory slicing depends on.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<AccountOwnership>> GetHistoryAsync(CancellationToken cancellationToken);
}
