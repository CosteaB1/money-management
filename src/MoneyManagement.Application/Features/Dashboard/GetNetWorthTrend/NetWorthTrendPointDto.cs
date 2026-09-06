namespace MoneyManagement.Application.Features.Dashboard.GetNetWorthTrend;

/// <summary>
/// One point on the net-worth trend line. <see cref="NetWorthMdl"/> is the sum
/// of every non-archived account's balance MINUS the external claims still
/// outstanding on that date (borrowed money is not wealth), all FX-converted to
/// MDL at the point's own as-of date. Accounts and claims that can't be
/// converted are omitted from the sum and trip the <see cref="MissingFxRate"/>
/// flag.
/// </summary>
/// <param name="Month">The point's calendar month label in <c>YYYY-MM</c> format.</param>
/// <param name="NetWorthMdl">Net-of-claims worth at the as-of date.</param>
/// <param name="MissingFxRate">True when at least one account or claim was omitted.</param>
public sealed record NetWorthTrendPointDto(
    string Month,
    decimal NetWorthMdl,
    bool MissingFxRate);
