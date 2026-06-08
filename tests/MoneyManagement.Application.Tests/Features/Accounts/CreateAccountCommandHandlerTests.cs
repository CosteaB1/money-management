using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts.CreateAccount;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Accounts;

public class CreateAccountCommandHandlerTests
{
    private static CreateAccountCommand ValidCommand(string? name = "Cash") => new(
        Name: name!,
        Type: AccountType.Cash,
        Balance: 100m,
        Currency: "MDL",
        OpeningDate: new DateOnly(2026, 1, 1),
        Notes: null);

    [Fact]
    public async Task Handle_WithValidCommand_PersistsAndReturnsNewId()
    {
        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        DbSet<Account> dbSet = Substitute.For<DbSet<Account>, IQueryable<Account>>();
        db.Accounts.Returns(dbSet);

        Account? captured = null;
        dbSet
            .When(s => s.Add(Arg.Any<Account>()))
            .Do(call => captured = call.Arg<Account>());

        var handler = new CreateAccountCommandHandler(db);

        Result<Guid> result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        result.Value.Should().Be(captured!.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNegativeBalanceOnCash_ReturnsDomainError()
    {
        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        DbSet<Account> dbSet = Substitute.For<DbSet<Account>, IQueryable<Account>>();
        db.Accounts.Returns(dbSet);

        var handler = new CreateAccountCommandHandler(db);

        CreateAccountCommand command = ValidCommand() with { Balance = -50m };
        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NegativeBalanceForNonCreditCard);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
