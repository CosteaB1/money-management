namespace MoneyManagement.Application.Features.Reports.GetBalanceOverTime;

/// <summary>
/// One point on the per-account balance trend. <see cref="Balance"/> is in the
/// account's native currency; <see cref="BalanceMdl"/> is the FX-converted
/// equivalent at <see cref="AsOf"/>, or null when no rate was available.
/// </summary>
public sealed record BalancePointDto(
    DateOnly AsOf,
    decimal Balance,
    decimal? BalanceMdl,
    bool MissingFxRate);
