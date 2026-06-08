using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Application.Features.FxRates.RefreshBnmRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.FxRates.BackfillBnmRates;

/// <summary>
/// Loops day-by-day over the requested range and delegates each date to the
/// existing single-date <see cref="RefreshBnmRatesCommand"/> handler, then
/// aggregates the per-date counts. Range guards live here (not in a
/// FluentValidation validator) because they depend on the clock.
/// </summary>
internal sealed class BackfillBnmRatesCommandHandler(
    ICommandHandler<RefreshBnmRatesCommand, RefreshBnmRatesResponse> refreshHandler,
    IDateTimeProvider clock)
    : ICommandHandler<BackfillBnmRatesCommand, BackfillBnmRatesResponse>
{
    public async Task<Result<BackfillBnmRatesResponse>> Handle(
        BackfillBnmRatesCommand command,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        DateOnly to = command.To ?? today;
        if (to > today)
        {
            to = today;
        }

        if (command.From > today)
        {
            return Result.Failure<BackfillBnmRatesResponse>(Error.Validation(
                "fx.backfill_future_start",
                "Backfill start date cannot be in the future."));
        }

        if (to < command.From)
        {
            return Result.Failure<BackfillBnmRatesResponse>(Error.Validation(
                "fx.backfill_invalid_range",
                "Backfill end date must be on or after the start date."));
        }

        if (to.DayNumber - command.From.DayNumber > 800)
        {
            return Result.Failure<BackfillBnmRatesResponse>(Error.Validation(
                "fx.backfill_range_too_large",
                "Backfill range cannot exceed about two years."));
        }

        int daysProcessed = 0;
        int fetched = 0;
        int inserted = 0;
        int updated = 0;
        int skipped = 0;

        for (DateOnly d = command.From; d <= to; d = d.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Result<RefreshBnmRatesResponse> r = await refreshHandler.Handle(
                new RefreshBnmRatesCommand(Date: d, CurrencyFilter: null),
                cancellationToken);

            if (r.IsFailure)
            {
                return Result.Failure<BackfillBnmRatesResponse>(r.Error);
            }

            fetched += r.Value.Fetched;
            inserted += r.Value.Inserted;
            updated += r.Value.Updated;
            skipped += r.Value.Skipped;
            daysProcessed++;
        }

        return Result.Success(new BackfillBnmRatesResponse(
            daysProcessed,
            fetched,
            inserted,
            updated,
            skipped));
    }
}
