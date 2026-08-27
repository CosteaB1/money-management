using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.UpdateLoan;

internal sealed class UpdateLoanCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateLoanCommand>
{
    public async Task<Result> Handle(UpdateLoanCommand command, CancellationToken cancellationToken)
    {
        // Default query filter: archived loans are not editable and 404 here.
        // The explicit predicate is defense-in-depth for unit tests that
        // bypass model configuration.
        Loan? loan = await db.Loans
            .FirstOrDefaultAsync(l => l.Id == command.Id && !l.IsArchived, cancellationToken);

        if (loan is null)
        {
            return Result.Failure(LoanErrors.NotFound(command.Id));
        }

        Result update = loan.Update(command.Counterparty, command.Notes);
        if (update.IsFailure)
        {
            return update;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
