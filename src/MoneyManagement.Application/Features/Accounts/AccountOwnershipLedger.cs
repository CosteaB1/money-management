using MoneyManagement.Application.Abstractions.NetWorth;

namespace MoneyManagement.Application.Features.Accounts;

/// <summary>
/// One materialized lookup of every account's ownership curve, so the net-worth
/// handlers can ask "what share of this account was the user's on date D?" once
/// per account per as-of date without touching a source again.
/// <para>
/// Sits beside <see cref="AccountBalanceLedger"/> and plays the same role for
/// the ownership seam that it plays for movements: load once, slice in memory.
/// It is a <b>caller-side helper, not a contract</b> — the contract is
/// <see cref="IAccountOwnershipSource"/> in <c>Abstractions/NetWorth/</c>. That
/// is why this type lives in <c>Features/Accounts/</c> and is internal: nothing
/// outside the Application layer's read handlers should depend on it.
/// </para>
/// </summary>
internal sealed class AccountOwnershipLedger
{
    private readonly Dictionary<Guid, AccountOwnership> _byAccountId;

    private AccountOwnershipLedger(Dictionary<Guid, AccountOwnership> byAccountId) =>
        _byAccountId = byAccountId;

    /// <summary>
    /// Drains every registered source once and indexes the result by account id.
    /// <para>
    /// Sources are injected as an <c>IEnumerable</c> so that the zero-source case
    /// — which is the whole system today — costs nothing and behaves exactly as
    /// if the seam did not exist: every account comes back wholly owned.
    /// </para>
    /// <para>
    /// If two sources claim the SAME account, the first one wins (registration
    /// order). Composing overlapping curves would mean multiplying two
    /// independently-dated step functions together, which is real arithmetic
    /// nobody has specified and no producer needs yet; first-wins keeps the
    /// result deterministic instead of order-dependent-and-silent.
    /// </para>
    /// </summary>
    public static async Task<AccountOwnershipLedger> LoadAsync(
        IEnumerable<IAccountOwnershipSource> sources,
        CancellationToken cancellationToken)
    {
        var byAccountId = new Dictionary<Guid, AccountOwnership>();

        foreach (IAccountOwnershipSource source in sources)
        {
            IReadOnlyList<AccountOwnership> ownerships = await source.GetHistoryAsync(cancellationToken);

            foreach (AccountOwnership ownership in ownerships)
            {
                byAccountId.TryAdd(ownership.AccountId, ownership);
            }
        }

        return new AccountOwnershipLedger(byAccountId);
    }

    /// <summary>
    /// The user's share of <paramref name="accountId"/> on
    /// <paramref name="asOf"/>, in <c>[0, 1]</c>.
    /// <para>
    /// An account no source mentions returns <c>1m</c> — wholly owned. "No
    /// ownership record" means "nobody else's money is in here", never "owns
    /// nothing"; the opposite default would zero out the entire dashboard the
    /// moment a source went missing.
    /// </para>
    /// </summary>
    public decimal OwnedFractionAsOf(Guid accountId, DateOnly asOf) =>
        _byAccountId.TryGetValue(accountId, out AccountOwnership? ownership)
            ? ownership.OwnedFractionAsOf(asOf)
            : 1m;
}
