namespace MoneyManagement.Application.Abstractions.NetWorth;

/// <summary>
/// Supplies the <see cref="ExternalClaim"/>s that net worth has to fold in on
/// top of the account balances. Loans are the only source today; anything else
/// that moves money into a tracked account without transferring ownership
/// (mortgages, pooled capital) plugs in here rather than into the dashboard
/// handlers.
/// </summary>
public interface IExternalClaimSource
{
    /// <summary>
    /// Every claim that has EVER existed, with its full settlement history, in
    /// one round-trip.
    /// <para>
    /// Intentionally not an "as of date D" query: the net-worth trend evaluates
    /// up to 24 as-of dates per request, and a per-point query would multiply
    /// the round-trips by 24. Callers materialize this once and slice it in
    /// memory via <see cref="ExternalClaim.OutstandingAsOf"/>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<ExternalClaim>> GetHistoryAsync(CancellationToken cancellationToken);
}
