namespace MoneyManagement.Application.Abstractions.NetWorth;

/// <summary>
/// Which way a claim pulls the user's net worth.
/// <see cref="ReducesNetWorth"/> — the user owes it (a liability);
/// <see cref="IncreasesNetWorth"/> — it is owed to the user (a receivable).
/// </summary>
public enum ExternalClaimSide
{
    ReducesNetWorth = 0,
    IncreasesNetWorth = 1,
}

/// <summary>
/// A dated, natively-denominated obligation that lives OUTSIDE the account
/// ledger but still belongs in net worth.
/// <para>
/// The account balances already reflect the cash that moved when the claim was
/// created (a received loan raised a balance); nothing in the ledger records
/// that the money has to go back. A claim closes that gap: it is the
/// counter-entry the double-entry books would have had.
/// </para>
/// <para>
/// Amounts are magnitudes, never signed — <see cref="Side"/> carries the
/// direction so callers can report liabilities and receivables separately
/// before netting them.
/// </para>
/// </summary>
/// <param name="Amount">The original obligation in <paramref name="Currency"/>. Always positive.</param>
/// <param name="Currency">ISO code the claim is denominated in — converted to MDL by the caller, at the caller's own as-of date.</param>
/// <param name="EffectiveFrom">The date the obligation came into existence. Before it, the claim contributes nothing.</param>
/// <param name="Side">Whether the claim subtracts from or adds to net worth.</param>
/// <param name="Settlements">Dated part-payments that shrink the claim, oldest-or-newest order irrelevant.</param>
public sealed record ExternalClaim(
    decimal Amount,
    string Currency,
    DateOnly EffectiveFrom,
    ExternalClaimSide Side,
    IReadOnlyList<ExternalClaimSettlement> Settlements)
{
    /// <summary>
    /// The still-owed magnitude on <paramref name="asOf"/>: zero before
    /// <see cref="EffectiveFrom"/>, otherwise <see cref="Amount"/> minus every
    /// settlement dated on or before that day.
    /// <para>
    /// Deliberately NOT clamped at zero. An over-settled claim comes back
    /// negative and callers skip it rather than silently flipping it into a
    /// receivable — inferring a reverse obligation from an overpayment is a
    /// product decision nobody has made.
    /// </para>
    /// </summary>
    public decimal OutstandingAsOf(DateOnly asOf)
    {
        if (EffectiveFrom > asOf)
        {
            return 0m;
        }

        decimal settled = 0m;
        foreach (ExternalClaimSettlement settlement in Settlements)
        {
            if (settlement.OccurredOn <= asOf)
            {
                settled += settlement.Amount;
            }
        }

        return Amount - settled;
    }
}

/// <summary>
/// One dated part-payment against an <see cref="ExternalClaim"/>, in the
/// claim's own currency (no cross-currency settlement in v1).
/// </summary>
public sealed record ExternalClaimSettlement(decimal Amount, DateOnly OccurredOn);
