using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Loans.ArchiveLoan;

internal sealed class ArchiveLoanCommandHandler(IApplicationDbContext db)
    : ICommandHandler<ArchiveLoanCommand>
{
    public async Task<Result> Handle(ArchiveLoanCommand command, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters so the same command can re-archive (idempotent
        // contract). The default filter hides archived loans; without this a
        // second DELETE call would 404 instead of being a no-op. Mirrors the
        // savings-goal archive handler.
        Loan? loan = await db.Loans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == command.Id, cancellationToken);

        if (loan is null)
        {
            return Result.Failure(LoanErrors.NotFound(command.Id));
        }

        Result archive = loan.Archive();
        if (archive.IsFailure)
        {
            return archive;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
