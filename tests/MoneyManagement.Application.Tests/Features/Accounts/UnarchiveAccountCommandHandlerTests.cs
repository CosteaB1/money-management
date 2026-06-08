using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts.UnarchiveAccount;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Accounts;

public class UnarchiveAccountCommandHandlerTests
{
    private static readonly DateOnly OpeningDate = new(2026, 1, 1);

    private static Account NewAccount()
    {
        Result<Account> result = Account.Create(
            "Cash Wallet",
            AccountType.Cash,
            new Money(500m, "MDL"),
            OpeningDate,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithExistingAccount_Unarchives()
    {
        Account account = NewAccount();
        account.Archive();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new UnarchiveAccountCommandHandler(db);

        Result result = await handler.Handle(new UnarchiveAccountCommand(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        account.IsArchived.Should().BeFalse();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithAlreadyActiveAccount_IsIdempotent()
    {
        Account account = NewAccount();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new UnarchiveAccountCommandHandler(db);

        Result result = await handler.Handle(new UnarchiveAccountCommand(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        account.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithMissingAccount_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new UnarchiveAccountCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(new UnarchiveAccountCommand(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingId));
    }
}
