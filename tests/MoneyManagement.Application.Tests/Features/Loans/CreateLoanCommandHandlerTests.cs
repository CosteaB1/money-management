using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Loans.CreateLoan;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Accounts;
using MoneyManagement.Domain.Categories;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

public class CreateLoanCommandHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly LoanDate = new(2026, 4, 15);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static Account NewAccount(string currency = "EUR", string name = "EUR Wallet")
    {
        Result<Account> result = Account.Create(
            name,
            AccountType.Cash,
            new Money(0m, currency),
            new DateOnly(2026, 1, 1),
            null);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static CreateLoanCommand NewCommand(
        LoanDirection direction = LoanDirection.Received,
        string counterparty = "Parents",
        decimal principal = 5_000m,
        string currency = "EUR",
        Guid? accountId = null,
        string? notes = null) =>
        new(direction, counterparty, principal, currency, LoanDate, accountId, notes);

    [Fact]
    public async Task Handle_WithoutAccount_PersistsLoanOnly()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateLoanCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(notes: "cash from parents"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Loan persisted = db.Loans.Single();
        persisted.Direction.Should().Be(LoanDirection.Received);
        persisted.Counterparty.Should().Be("Parents");
        persisted.Principal.Amount.Should().Be(5_000m);
        persisted.Principal.Currency.Should().Be("EUR");
        persisted.LoanDate.Should().Be(LoanDate);
        persisted.Notes.Should().Be("cash from parents");
        persisted.DisbursementTransactionId.Should().BeNull();
        result.Value.Id.Should().Be(persisted.Id);
        db.Transactions.Should().BeEmpty();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReceivedWithAccount_SynthesizesIncomeDisbursement()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CreateLoanCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(accountId: account.Id, notes: "wired over"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = db.Transactions.Single();
        transaction.AccountId.Should().Be(account.Id);
        transaction.Direction.Should().Be(TransactionDirection.Income);
        transaction.Description.Should().Be("Loan from Parents");
        transaction.Amount.Amount.Should().Be(5_000m);
        transaction.Amount.Currency.Should().Be("EUR");
        transaction.TransactionDate.Should().Be(LoanDate);
        transaction.IsTransfer.Should().BeTrue();
        transaction.IsAdjustment.Should().BeFalse();
        transaction.CounterAccountId.Should().BeNull();
        transaction.CategoryId.Should().Be(SeededCategories.LoanId);
        transaction.Source.Should().Be(TransactionSource.Manual);
        transaction.Notes.Should().Be("wired over");

        Loan loan = db.Loans.Single();
        loan.DisbursementTransactionId.Should().Be(transaction.Id);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GivenWithAccount_SynthesizesExpenseDisbursement()
    {
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CreateLoanCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(
                direction: LoanDirection.Given,
                counterparty: "Ion",
                principal: 3_000m,
                currency: "MDL",
                accountId: account.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = db.Transactions.Single();
        transaction.Direction.Should().Be(TransactionDirection.Expense);
        transaction.Description.Should().Be("Loan to Ion");
        transaction.IsTransfer.Should().BeTrue();
        transaction.CategoryId.Should().Be(SeededCategories.LoanId);
    }

    [Fact]
    public async Task Handle_WithAccount_PassesMdlConversionThroughToCreatedEvent()
    {
        Account account = NewAccount();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        IFxConverter fx = FakeFxConverter.WithTable(new Dictionary<string, decimal> { ["EUR"] = 19.5m });
        var handler = new CreateLoanCommandHandler(db, fx, Clock());

        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(accountId: account.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Transaction transaction = db.Transactions.Single();
        AmountMdlOf(transaction).Should().Be(5_000m * 19.5m);
        await fx.Received(1).ConvertAsync(
            5_000m, "EUR", "MDL", LoanDate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AccountCurrencyMismatch_FailsWithoutPersisting()
    {
        Account account = NewAccount(currency: "MDL");
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CreateLoanCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(currency: "EUR", accountId: account.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.AccountCurrencyMismatch);
        db.Loans.Should().BeEmpty();
        db.Transactions.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AccountMissing_ReturnsAccountNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateLoanCommandHandler(db, FakeFxConverter.Identity(), Clock());

        var missingId = Guid.CreateVersion7();
        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(accountId: missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(missingId));
        db.Loans.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AccountArchived_ReturnsAccountNotFound()
    {
        Account account = NewAccount();
        account.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(accounts: [account]);
        var handler = new CreateLoanCommandHandler(db, FakeFxConverter.Identity(), Clock());

        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(accountId: account.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AccountErrors.NotFound(account.Id));
        db.Loans.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidLoanInput_BubblesDomainError()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateLoanCommandHandler(db, FakeFxConverter.Identity(), Clock());

        // The FluentValidation decorator catches this in the real pipeline;
        // hitting the handler directly confirms the domain safety net fires.
        Result<CreateLoanResponse> result = await handler.Handle(
            NewCommand(counterparty: "   "), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.CounterpartyRequired);
        db.Loans.Should().BeEmpty();
    }

    private static decimal? AmountMdlOf(Transaction transaction) =>
        transaction.GetDomainEvents()
            .OfType<TransactionCreatedDomainEvent>()
            .Single()
            .AmountMdl;
}
