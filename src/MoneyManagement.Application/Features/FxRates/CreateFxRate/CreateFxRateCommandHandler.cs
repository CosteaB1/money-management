using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.FxRates.CreateFxRate;

internal sealed class CreateFxRateCommandHandler(IApplicationDbContext db)
    : ICommandHandler<CreateFxRateCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateFxRateCommand command, CancellationToken cancellationToken)
    {
        Result<FxRate> rateResult = FxRate.Create(
            command.FromCurrency,
            command.ToCurrency,
            command.Rate,
            command.AsOf);

        if (rateResult.IsFailure)
        {
            return Result.Failure<Guid>(rateResult.Error);
        }

        FxRate rate = rateResult.Value;
        db.FxRates.Add(rate);
        await db.SaveChangesAsync(cancellationToken);

        return rate.Id;
    }
}
