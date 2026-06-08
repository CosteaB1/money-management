using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Transactions.CreateTransfer;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class CreateTransferCommandHandlerTests
{
    private static readonly DateOnly TransferDate = new(2026, 4, 15);
    private static readonly DateOnly OpeningDate = new(2026, 1, 1);

    private static Account NewAccount(string name, string currency = "MDL", decimal opening = 1_000m)
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(opening, currency),
            OpeningDate,
            notes: null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static CreateTransferCommand ValidCommand(Guid source, Guid destination) => new(
        SourceAccountId: source,
        DestinationAccountId: destination,
        Amount: 250m,
        Date: TransferDate,
        Description: "Salary card -> daily card",
        CategoryId: null);

    [Fact]
    public async Task Handle_WithValidCommand_PersistsBothLegsAsTransfers()
    {
        Account source = NewAccount("Salary");
        Account destination = NewAccount("Daily");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, destination]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        Result<TransferResult> result = await handler.Handle(
            ValidCommand(source.Id, destination.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceTransactionId.Should().NotBeEmpty();
        result.Value.DestinationTransactionId.Should().NotBeEmpty();
        result.Value.SourceTransactionId.Should().NotBe(result.Value.DestinationTransactionId);

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);
        persisted.Should().AllSatisfy(t => t.IsTransfer.Should().BeTrue());
        persisted.Should().AllSatisfy(t => t.Source.Should().Be(TransactionSource.Manual));

        Transaction sourceLeg = persisted.Single(t => t.AccountId == source.Id);
        sourceLeg.Direction.Should().Be(TransactionDirection.Expense);
        sourceLeg.CounterAccountId.Should().Be(destination.Id);
        sourceLeg.Amount.Amount.Should().Be(250m);

        Transaction destinationLeg = persisted.Single(t => t.AccountId == destination.Id);
        destinationLeg.Direction.Should().Be(TransactionDirection.Income);
        destinationLeg.CounterAccountId.Should().Be(source.Id);
        destinationLeg.Amount.Amount.Should().Be(250m);
    }

    [Fact]
    public async Task Handle_WithNotes_PersistsSameNoteOnBothLegs()
    {
        Account source = NewAccount("Salary");
        Account destination = NewAccount("Daily");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, destination]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        var command = new CreateTransferCommand(
            SourceAccountId: source.Id,
            DestinationAccountId: destination.Id,
            Amount: 250m,
            Date: TransferDate,
            Description: "Salary card -> daily card",
            CategoryId: null,
            Notes: "moving rent money");

        Result<TransferResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);

        Transaction sourceLeg = persisted.Single(t => t.AccountId == source.Id);
        sourceLeg.Direction.Should().Be(TransactionDirection.Expense);
        sourceLeg.Notes.Should().Be("moving rent money");

        Transaction destinationLeg = persisted.Single(t => t.AccountId == destination.Id);
        destinationLeg.Direction.Should().Be(TransactionDirection.Income);
        destinationLeg.Notes.Should().Be("moving rent money");
    }

    [Fact]
    public async Task Handle_WithSameSourceAndDestination_Fails()
    {
        Account account = NewAccount("Solo");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        Result<TransferResult> result = await handler.Handle(
            ValidCommand(account.Id, account.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransferErrors.SameSourceAndDestination);
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithMissingSourceAccount_Fails()
    {
        Account destination = NewAccount("Daily");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [destination]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        var missingSource = Guid.CreateVersion7();
        Result<TransferResult> result = await handler.Handle(
            ValidCommand(missingSource, destination.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransferErrors.SourceAccountNotFound(missingSource));
    }

    [Fact]
    public async Task Handle_WithMissingDestinationAccount_Fails()
    {
        Account source = NewAccount("Salary");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        var missingDestination = Guid.CreateVersion7();
        Result<TransferResult> result = await handler.Handle(
            ValidCommand(source.Id, missingDestination),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransferErrors.DestinationAccountNotFound(missingDestination));
    }

    [Fact]
    public async Task Handle_CrossCurrencyWithDestinationAmount_PersistsBothLegsInOwnCurrencies()
    {
        // 17,163 MDL leaves the source; 1000 USD arrives on the destination.
        // Each leg is denominated in its own account's currency, both share the
        // SAME source-derived MDL value, and Original* are cross-stamped.
        Account source = NewAccount("MAIB", currency: "MDL");
        Account destination = NewAccount("Bybit", currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, destination]);

        // Source side converts MDL->MDL (identity) so amountMdl == source amount.
        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        var command = new CreateTransferCommand(
            SourceAccountId: source.Id,
            DestinationAccountId: destination.Id,
            Amount: 17_163m,
            Date: TransferDate,
            Description: "MAIB MDL -> Bybit USD",
            CategoryId: null,
            DestinationAmount: 1_000m);

        Result<TransferResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);

        Transaction sourceLeg = persisted.Single(t => t.AccountId == source.Id);
        sourceLeg.Direction.Should().Be(TransactionDirection.Expense);
        sourceLeg.Amount.Amount.Should().Be(17_163m);
        sourceLeg.Amount.Currency.Should().Be("MDL");
        // Source leg carries the OTHER (destination) leg's amount+currency.
        sourceLeg.OriginalAmount.Should().Be(1_000m);
        sourceLeg.OriginalCurrency.Should().Be("USD");

        Transaction destinationLeg = persisted.Single(t => t.AccountId == destination.Id);
        destinationLeg.Direction.Should().Be(TransactionDirection.Income);
        destinationLeg.Amount.Amount.Should().Be(1_000m);
        destinationLeg.Amount.Currency.Should().Be("USD");
        // Destination leg carries the source leg's amount+currency.
        destinationLeg.OriginalAmount.Should().Be(17_163m);
        destinationLeg.OriginalCurrency.Should().Be("MDL");

        // Both legs share the same source-derived MDL value (conservation).
        decimal? sourceMdl = AmountMdlOf(sourceLeg);
        decimal? destinationMdl = AmountMdlOf(destinationLeg);
        sourceMdl.Should().Be(17_163m);
        destinationMdl.Should().Be(sourceMdl);
    }

    [Fact]
    public async Task Handle_CrossCurrencyWithoutDestinationAmount_Fails()
    {
        Account source = NewAccount("MDL acct", currency: "MDL");
        Account destination = NewAccount("USD acct", currency: "USD");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, destination]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        // DestinationAmount omitted -> cross-currency transfer is rejected.
        Result<TransferResult> result = await handler.Handle(
            ValidCommand(source.Id, destination.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransferErrors.DestinationAmountRequired);
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SameCurrency_IgnoresDestinationAmount_AndLeavesOriginalNull()
    {
        Account source = NewAccount("Salary");
        Account destination = NewAccount("Daily");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, destination]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        // Same currency: a stray DestinationAmount must be ignored; both legs
        // use Amount, and Original* stay null (current behavior).
        var command = new CreateTransferCommand(
            SourceAccountId: source.Id,
            DestinationAccountId: destination.Id,
            Amount: 250m,
            Date: TransferDate,
            Description: "MDL -> MDL",
            CategoryId: null,
            DestinationAmount: 9_999m);

        Result<TransferResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().HaveCount(2);

        Transaction sourceLeg = persisted.Single(t => t.AccountId == source.Id);
        Transaction destinationLeg = persisted.Single(t => t.AccountId == destination.Id);

        sourceLeg.Amount.Amount.Should().Be(250m);
        destinationLeg.Amount.Amount.Should().Be(250m);
        sourceLeg.OriginalAmount.Should().BeNull();
        sourceLeg.OriginalCurrency.Should().BeNull();
        destinationLeg.OriginalAmount.Should().BeNull();
        destinationLeg.OriginalCurrency.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithExistingCategory_PersistsBothLegsWithCategory()
    {
        Account source = NewAccount("Salary");
        Account destination = NewAccount("Daily");
        Domain.Categories.Category category =
            Domain.Categories.Category.Create("Transfers", Domain.Categories.CategoryFlow.Expense).Value;
        IApplicationDbContext db = FakeApplicationDbContext.Create(
            accounts: [source, destination],
            categories: [category]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        CreateTransferCommand command = ValidCommand(source.Id, destination.Id) with { CategoryId = category.Id };
        Result<TransferResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        List<Transaction> persisted = [.. db.Transactions];
        persisted.Should().AllSatisfy(t => t.CategoryId.Should().Be(category.Id));
    }

    [Fact]
    public async Task Handle_WithMissingCategory_ReturnsCategoryNotFound()
    {
        Account source = NewAccount("Salary");
        Account destination = NewAccount("Daily");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, destination]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());

        var missingCategory = Guid.CreateVersion7();
        CreateTransferCommand command = ValidCommand(source.Id, destination.Id) with { CategoryId = missingCategory };
        Result<TransferResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Domain.Categories.CategoryErrors.NotFound(missingCategory));
        db.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenSourceLegFactoryFails_PropagatesError_WithoutPersisting()
    {
        // A description over the domain max makes Transaction.Create fail for the
        // source leg. The validator normally blocks this; at the handler level the
        // factory's guard is the last line of defence and must propagate.
        Account source = NewAccount("Salary");
        Account destination = NewAccount("Daily");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [source, destination]);

        var handler = new CreateTransferCommandHandler(db, FakeFxConverter.Identity());
        CreateTransferCommand command = ValidCommand(source.Id, destination.Id) with
        {
            Description = new string('x', Transaction.DescriptionMaxLength + 1),
        };

        Result<TransferResult> result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.DescriptionTooLong);
        db.Transactions.Should().BeEmpty();
    }

    private static decimal? AmountMdlOf(Transaction transaction) =>
        transaction.GetDomainEvents()
            .OfType<TransactionCreatedDomainEvent>()
            .Single()
            .AmountMdl;
}
