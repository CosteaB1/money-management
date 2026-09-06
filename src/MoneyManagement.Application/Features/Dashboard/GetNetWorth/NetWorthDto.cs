namespace MoneyManagement.Application.Features.Dashboard.GetNetWorth;

/// <summary>
/// Today's net worth, split into the pieces the dashboard needs to explain the
/// number rather than just print it.
/// <para>
/// Gross assets alone counted borrowed money as wealth: a received loan raised
/// an account balance and nothing anywhere subtracted the obligation. The two
/// claim legs close that gap.
/// </para>
/// <para>
/// Every field is in MDL at TODAY's rate. Unconvertible accounts and claims are
/// omitted from the totals — no implicit 1:1 fallback — and surface through the
/// two counters so the UI can say how much of the picture is missing.
/// </para>
/// </summary>
/// <param name="GrossAssetsMdl">Sum of every non-archived account balance.</param>
/// <param name="ExternalLiabilitiesMdl">Outstanding on qualifying received loans, as a POSITIVE magnitude.</param>
/// <param name="ExternalAssetsMdl">Outstanding on qualifying given loans, as a POSITIVE magnitude.</param>
/// <param name="NetWorthMdl"><c>GrossAssetsMdl - ExternalLiabilitiesMdl + ExternalAssetsMdl</c>.</param>
/// <param name="AccountsMissingFxRate">How many non-archived accounts were omitted for want of a rate.</param>
/// <param name="LoansMissingFxRate">How many outstanding claims were omitted for want of a rate.</param>
public sealed record NetWorthDto(
    decimal GrossAssetsMdl,
    decimal ExternalLiabilitiesMdl,
    decimal ExternalAssetsMdl,
    decimal NetWorthMdl,
    int AccountsMissingFxRate,
    int LoansMissingFxRate);
