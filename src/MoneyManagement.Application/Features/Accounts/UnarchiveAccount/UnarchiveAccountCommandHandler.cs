using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.UnarchiveAccount;

internal sealed class UnarchiveAccountCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UnarchiveAccountCommand>
{
    public async Task<Result> Handle(UnarchiveAccountCommand command, CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken);

        if (account is null)
        {
            return Result.Failure(AccountErrors.NotFound(command.Id));
        }

        account.Unarchive();
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
