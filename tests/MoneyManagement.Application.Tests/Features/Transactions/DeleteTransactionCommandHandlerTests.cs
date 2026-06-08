using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Abstractions.FxRates;
using MoneyManagement.Application.Features.Transactions.DeleteTransaction;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class DeleteTransactionCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Transaction NewTransaction(
        decimal amount = 100m,
        string currency = ReportingCurrencies.Mdl,
        DateOnly? date = null) =>
        Transaction.Create(
            AccountId,
            date ?? Today,
            TransactionDirection.Expense,
            new Money(amount, currency),
            "Coffee",
            TransactionSource.Manual).Value;

    [Fact]
    public async Task Handle_WithExistingTransaction_SoftDeletes()
    {
        Transaction transaction = NewTransaction();
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [transaction]);

        var handler = new DeleteTransactionCommandHandler(db, FakeFxConverter.Identity());
        Result result = await handler.Handle(new DeleteTransactionCommand(transaction.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.IsDeleted.Should().BeTrue();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingTransaction_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new DeleteTransactionCommandHandler(db, FakeFxConverter.Identity());
        var unknownId = Guid.CreateVersion7();

        Result result = await handler.Handle(new DeleteTransactionCommand(unknownId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.NotFound(unknownId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvokesFxConverterWithTransactionDateAndCurrency()
    {
        var txDate = new DateOnly(2026, 4, 15);
        Transaction transaction = NewTransaction(amount: 120m, currency: "USD", date: txDate);
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [transaction]);

        IFxConverter fx = FakeFxConverter.Identity();
        var handler = new DeleteTransactionCommandHandler(db, fx);

        await handler.Handle(new DeleteTransactionCommand(transaction.Id), CancellationToken.None);

        await fx.Received(1).ConvertAsync(
            120m,
            "USD",
            ReportingCurrencies.Mdl,
            txDate,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnAlreadyDeletedRow_StillSucceeds_AndDoesNotRaiseSecondEvent()
    {
        // Soft-deleted rows are hidden in production by the query filter, but
        // FakeApplicationDbContext bypasses EF model config so the row stays
        // reachable. We rely on Transaction.MarkDeleted's own idempotence.
        Transaction transaction = NewTransaction();
        transaction.MarkDeleted();
        transaction.ClearDomainEvents();
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [transaction]);

        var handler = new DeleteTransactionCommandHandler(db, FakeFxConverter.Identity());
        Result result = await handler.Handle(new DeleteTransactionCommand(transaction.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.IsDeleted.Should().BeTrue();
        transaction.GetDomainEvents()
            .OfType<TransactionDeletedDomainEvent>()
            .Should().BeEmpty();
    }
}
