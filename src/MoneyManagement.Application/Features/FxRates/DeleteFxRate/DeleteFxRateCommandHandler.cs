using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.FxRates.DeleteFxRate;

internal sealed class DeleteFxRateCommandHandler(IApplicationDbContext db)
    : ICommandHandler<DeleteFxRateCommand>
{
    public async Task<Result> Handle(DeleteFxRateCommand command, CancellationToken cancellationToken)
    {
        FxRate? rate = await db.FxRates
            .FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken);

        if (rate is null)
        {
            return Result.Failure(FxRateErrors.NotFound(command.Id));
        }

        // FxRate is reference data with no historical references - hard delete is fine.
        db.FxRates.Remove(rate);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
