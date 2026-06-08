using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.FxRates.ConvertFx;

internal sealed class ConvertFxQueryHandler(IFxConverter fxConverter)
    : IQueryHandler<ConvertFxQuery, ConvertFxResult>
{
    public async Task<Result<ConvertFxResult>> Handle(
        ConvertFxQuery query,
        CancellationToken cancellationToken)
    {
        if (!CurrencyCodes.IsValidIso(query.From) || !CurrencyCodes.IsValidIso(query.To))
        {
            return Result.Failure<ConvertFxResult>(
                Error.Validation(
                    "fx.invalid_currency",
                    "From and To must be 3-letter uppercase ISO currency codes."));
        }

        // Identity short-circuit: the converter already returns the amount
        // unchanged for from==to, but we want a derived rate of exactly 1.
        if (string.Equals(query.From, query.To, StringComparison.Ordinal))
        {
            return new ConvertFxResult(query.Amount, 1m, HasRate: true);
        }

        decimal? converted = await fxConverter.ConvertAsync(
            query.Amount,
            query.From,
            query.To,
            query.Date,
            cancellationToken);

        if (converted is null)
        {
            return new ConvertFxResult(null, null, HasRate: false);
        }

        // Effective rate is derived (converted/amount); guard against /0.
        decimal? rate = query.Amount == 0m ? null : converted.Value / query.Amount;

        return new ConvertFxResult(converted, rate, HasRate: true);
    }
}
