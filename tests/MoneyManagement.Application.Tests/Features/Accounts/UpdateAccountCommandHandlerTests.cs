using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Accounts.UpdateAccount;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Accounts;

public class UpdateAccountCommandHandlerTests
{
    private static readonly DateOnly OpeningDate = new(2026, 1, 1);

    private static Account NewAccount()
    {
        Result<Account> result = Account.Create(
            "Cash Wallet",
            AccountType.Cash,
            new Money(500m, "MDL"),
            OpeningDate,
            notes: "old notes");

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithExistingAccount_RenamesAndPersists()
    {
        Account account = NewAccount();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new UpdateAccountCommandHandler(db);

        Result result = await handler.Handle(
            new UpdateAccountCommand(account.Id, "Renamed Wallet", "new notes"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        account.Name.Should().Be("Renamed Wallet");
        account.Notes.Should().Be("new notes");
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithInvalidName_ReturnsDomainFailureAndDoesNotPersist()
    {
        Account account = NewAccount();

        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new UpdateAccountCommandHandler(db);

        Result result = await handler.Handle(
            new UpdateAccountCommand(account.Id, "   ", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NameRequired);
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingAccount_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new UpdateAccountCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(
            new UpdateAccountCommand(missingId, "Whatever", null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
