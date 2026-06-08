using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.ArchiveAccount;

internal sealed class ArchiveAccountCommandHandler(IApplicationDbContext db)
    : ICommandHandler<ArchiveAccountCommand>
{
    public async Task<Result> Handle(ArchiveAccountCommand command, CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken);

        if (account is null)
        {
            return Result.Failure(AccountErrors.NotFound(command.Id));
        }

        account.Archive();
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
