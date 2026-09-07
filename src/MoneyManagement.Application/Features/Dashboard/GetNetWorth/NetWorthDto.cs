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
/// <param name="GrossAssetsMdl">
/// <b>The user's share</b> of every non-archived account balance. Where an
/// <c>IAccountOwnershipSource</c> reports outside capital in an account (pooled
/// money held on someone else's behalf), only the owned fraction lands here —
/// the rest is never counted as the user's asset in the first place, so nothing
/// downstream has to subtract it back out. With no ownership source registered
/// every account is wholly owned and this is the plain sum it always was.
/// </param>
/// <param name="ExternalLiabilitiesMdl">Outstanding on qualifying received loans, as a POSITIVE magnitude.</param>
/// <param name="ExternalAssetsMdl">Outstanding on qualifying given loans, as a POSITIVE magnitude.</param>
/// <param name="NetWorthMdl"><c>GrossAssetsMdl - ExternalLiabilitiesMdl + ExternalAssetsMdl</c>.</param>
/// <param name="AccountsMissingFxRate">How many non-archived accounts were omitted for want of a rate.</param>
/// <param name="LoansMissingFxRate">How many outstanding claims were omitted for want of a rate.</param>
/// <param name="OutsideCapitalMdl">
/// The complement of the owned share: other people's money sitting inside the
/// user's non-archived accounts, as a POSITIVE magnitude.
/// <para>
/// Deliberately OUTSIDE the net-worth identity — <see cref="NetWorthMdl"/> stays
/// exactly <c>GrossAssetsMdl − ExternalLiabilitiesMdl + ExternalAssetsMdl</c>.
/// This figure is already excluded from <see cref="GrossAssetsMdl"/>, so adding
/// or subtracting it anywhere in that equation would double-count. It exists
/// purely so the UI can explain the gap between an account's displayed balance
/// and its contribution to net worth.
/// </para>
/// <para>
/// It is reported over CONVERTIBLE accounts only, matching
/// <see cref="GrossAssetsMdl"/>: an account with no usable rate drops out of
/// both legs and trips <see cref="AccountsMissingFxRate"/> instead.
/// </para>
/// <para>
/// Trailing and defaulted so the record stays additive for existing
/// construction sites; the handler always passes it explicitly.
/// </para>
/// </param>
public sealed record NetWorthDto(
    decimal GrossAssetsMdl,
    decimal ExternalLiabilitiesMdl,
    decimal ExternalAssetsMdl,
    decimal NetWorthMdl,
    int AccountsMissingFxRate,
    int LoansMissingFxRate,
    decimal OutsideCapitalMdl);
