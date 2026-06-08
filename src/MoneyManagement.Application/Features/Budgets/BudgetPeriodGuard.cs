using System.Diagnostics.CodeAnalysis;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Budgets;

/// <summary>
/// Shared "this can never fail" guard for the budget-period event handlers.
/// <para>
/// The handlers create <c>BudgetPeriod</c> rows from a domain event's
/// <c>TransactionDate</c> (always a valid year/month) and add/subtract spend
/// amounts that the handler's own <c>ShouldSkip</c> filter has already proven
/// positive. The wrapped domain operations therefore cannot fail in practice —
/// but a future change that loosens those guarantees should crash loudly during
/// development rather than silently corrupting a budget rollup. This helper
/// centralises that intent so the (genuinely unreachable) throw lives in one
/// place.
/// </para>
/// </summary>
internal static class BudgetPeriodGuard
{
    /// <summary>
    /// Throws if <paramref name="result"/> is a failure. By construction the
    /// callers never pass a failing result, so the throw is unreachable in
    /// practice and excluded from coverage.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification =
        "Defensive 'this can never fail' guard. Callers only pass domain results " +
        "that are guaranteed to succeed (valid year/month from a DateOnly, " +
        "pre-filtered positive amounts); the throw is unreachable by construction.")]
    public static void EnsureSucceeded(Result result, string context)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{context}: {result.Error.Message}");
        }
    }
}
