using MoneyManagement.Application.Abstractions.Messaging;

namespace MoneyManagement.Application.Features.FxRates.BackfillBnmRates;

/// <summary>
/// Backfills BNM rates over an inclusive date range by replaying the
/// per-date <see cref="RefreshBnmRates.RefreshBnmRatesCommand"/> one day at a
/// time. Weekend/holiday days are harmless no-ops (the per-date handler
/// returns success with zero counts). Manual rates always win — the
/// underlying refresh skips them on collision.
/// </summary>
/// <param name="From">First day to backfill (inclusive).</param>
/// <param name="To">
/// Last day to backfill (inclusive); <c>null</c> means today UTC. Clamped to
/// today if it lands in the future.
/// </param>
public sealed record BackfillBnmRatesCommand(DateOnly From, DateOnly? To = null)
    : ICommand<BackfillBnmRatesResponse>;

/// <summary>
/// Aggregate counts across every date in the backfill range.
/// </summary>
/// <param name="DaysProcessed">Number of dates the per-date refresh ran for.</param>
/// <param name="Fetched">Sum of rates returned by BNM across all dates.</param>
/// <param name="Inserted">Sum of rows newly inserted as BnmAuto.</param>
/// <param name="Updated">Sum of existing BnmAuto rows refreshed.</param>
/// <param name="Skipped">Sum of skipped rates (manual wins, unchanged, or not held).</param>
public sealed record BackfillBnmRatesResponse(
    int DaysProcessed,
    int Fetched,
    int Inserted,
    int Updated,
    int Skipped);
