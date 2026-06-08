using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Transactions.CreateTransaction;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class CreateTransactionCommandHandlerTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly OpeningDate = new(2026, 1, 1);

    private static Account NewAccount(string currency = "MDL", AccountType type = AccountType.Cash)
    {
        Result<Account> result = Account.Create(
            $"{currency} wallet",
            type,
            new Money(1_000m, currency),
            OpeningDate,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static CreateTransactionCommand ValidCommand(Guid accountId, string? currency = null) => new(
        AccountId: accountId,
        TransactionDate: Today,
        Direction: TransactionDirection.Expense,
        Amount: 50m,
        Description: "Coffee",
        CategoryId: null,
        OriginalAmount: null,
        OriginalCurrency: null,
        IsTransfer: false,
        CounterAccountId: null,
        IsAdjustment: false,
        Currency: currency);

    [Fact]
    public async Task Handle_WhenCurrencyOmitted_InheritsAccountCurrency()
    {
        Account account = NewAccount(currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CreateTransactionCommandHandler(db, FakeFxConverter.Identity());

        Result<Guid> result = await handler.Handle(ValidCommand(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction persisted = db.Transactions.Single();
        persisted.Amount.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_WithCurrencyMatchingAccount_Succeeds()
    {
        Account account = NewAccount(currency: "EUR");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CreateTransactionCommandHandler(db, FakeFxConverter.Identity());

        Result<Guid> result = await handler.Handle(
            ValidCommand(account.Id, currency: "EUR"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithCurrencyMismatchingAccount_Fails()
    {
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CreateTransactionCommandHandler(db, FakeFxConverter.Identity());

        Result<Guid> result = await handler.Handle(
            ValidCommand(account.Id, currency: "USD"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.CurrencyMismatchAccount);
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithNotes_PersistsTrimmedNotes()
    {
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CreateTransactionCommandHandler(db, FakeFxConverter.Identity());
        CreateTransactionCommand command = ValidCommand(account.Id) with { Notes = "  Split with Maria  " };

        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction persisted = db.Transactions.Single();
        persisted.Notes.Should().Be("Split with Maria");
    }

    [Fact]
    public async Task Handle_WithoutNotes_LeavesNotesNull()
    {
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CreateTransactionCommandHandler(db, FakeFxConverter.Identity());

        Result<Guid> result = await handler.Handle(ValidCommand(account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        db.Transactions.Single().Notes.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithMissingAccount_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateTransactionCommandHandler(db, FakeFxConverter.Identity());

        var missingId = Guid.CreateVersion7();
        Result<Guid> result = await handler.Handle(ValidCommand(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingId));
    }

    [Fact]
    public async Task Handle_WithDomainInvalidInput_ReturnsDomainFailure_AndDoesNotSave()
    {
        // Account/category/currency checks pass, but an over-long description
        // fails Transaction.Create (shadowed by the validator in the pipeline).
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CreateTransactionCommandHandler(db, FakeFxConverter.Identity());

        string longDescription = new string('d', Transaction.DescriptionMaxLength + 1);
        CreateTransactionCommand command = ValidCommand(account.Id) with { Description = longDescription };

        Result<Guid> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.DescriptionTooLong);
        db.Transactions.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
