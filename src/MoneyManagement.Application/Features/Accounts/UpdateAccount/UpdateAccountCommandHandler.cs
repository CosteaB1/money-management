using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.UpdateAccount;

internal sealed class UpdateAccountCommandHandler(IApplicationDbContext db)
    : ICommandHandler<UpdateAccountCommand>
{
    public async Task<Result> Handle(UpdateAccountCommand command, CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken);

        if (account is null)
        {
            return Result.Failure(AccountErrors.NotFound(command.Id));
        }

        Result update = account.Update(command.Name, command.Notes);
        if (update.IsFailure)
        {
            return update;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
