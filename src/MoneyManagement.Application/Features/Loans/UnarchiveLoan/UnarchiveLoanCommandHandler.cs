using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.UnarchiveLoan;

internal sealed class UnarchiveLoanCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UnarchiveLoanCommand>
{
    public async Task<Result> Handle(UnarchiveLoanCommand command, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters is the whole point here: the default filter hides
        // archived loans, and unarchive must find exactly those rows.
        // Unarchiving an already-active loan is an idempotent success, not a
        // 404 — mirrors the archive handler's contract.
        Loan? loan = await db.Loans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == command.Id, cancellationToken);

        if (loan is null)
        {
            return Result.Failure(LoanErrors.NotFound(command.Id));
        }

        Result unarchive = loan.Unarchive();
        if (unarchive.IsFailure)
        {
            return unarchive;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
