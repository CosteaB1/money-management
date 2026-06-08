using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Features.Accounts.CreateAccount;

internal sealed class CreateAccountCommandHandler(IApplicationDbContext db)
    : ICommandHandler<CreateAccountCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateAccountCommand command, CancellationToken cancellationToken)
    {
        var balance = new Money(command.Balance, command.Currency);

        Result<Account> accountResult = Account.Create(
            command.Name,
            command.Type,
            balance,
            command.OpeningDate,
            command.Notes);

        if (accountResult.IsFailure)
        {
            return Result.Failure<Guid>(accountResult.Error);
        }

        Account account = accountResult.Value;
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        return account.Id;
    }
}
