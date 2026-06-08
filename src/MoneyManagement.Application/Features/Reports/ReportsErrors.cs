using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Reports;

/// <summary>
/// Application-level errors for the read-only Reports slice. Lives in
/// Application (not Domain) because Reports has no entity — it's a pure
/// projection over <see cref="MoneyManagement.Domain.Transactions.Transaction"/>
/// and <see cref="MoneyManagement.Domain.Accounts.Account"/>.
/// </summary>
public static class ReportsErrors
{
    public static Error RangeOutOfBounds(string detail) =>
        Error.Validation("reports.range_out_of_bounds", detail);

    public static Error IntervalTooFine(string detail) =>
        Error.Validation("reports.interval_too_fine", detail);
}
