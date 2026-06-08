using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Dashboard;

/// <summary>
/// Application-level errors for the read-only Dashboard slice.
/// Lives in Application (not Domain) because Dashboard has no entity — it's a
/// pure projection over <see cref="MoneyManagement.Domain.Transactions.Transaction"/>
/// and <see cref="MoneyManagement.Domain.Accounts.Account"/>.
/// </summary>
public static class DashboardErrors
{
    public static Error MonthsOutOfRange(int min, int max) =>
        Error.Validation(
            "dashboard.months_out_of_range",
            $"Months must be between {min} and {max} inclusive.");
}
