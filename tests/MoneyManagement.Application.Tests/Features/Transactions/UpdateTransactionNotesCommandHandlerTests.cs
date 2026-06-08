using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Transactions.UpdateTransactionNotes;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class UpdateTransactionNotesCommandHandlerTests
{
    private static readonly Guid AccountId = Guid.CreateVersion7();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Transaction NewTransaction(string? notes = null)
    {
        Result<Transaction> result = Transaction.Create(
            AccountId,
            Today,
            TransactionDirection.Expense,
            new Money(100m, ReportingCurrencies.Mdl),
            "Coffee",
            TransactionSource.Manual,
            notes: notes);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_WithNotes_SetsTrimmedNotes()
    {
        Transaction transaction = NewTransaction();
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [transaction]);

        var handler = new UpdateTransactionNotesCommandHandler(db);
        var command = new UpdateTransactionNotesCommand(transaction.Id, "  Reimbursed by Alex  ");

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.Notes.Should().Be("Reimbursed by Alex");
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNullNotes_ClearsNotes()
    {
        Transaction transaction = NewTransaction(notes: "Existing note");
        IApplicationDbContext db = FakeApplicationDbContext.Create(transactions: [transaction]);

        var handler = new UpdateTransactionNotesCommandHandler(db);
        var command = new UpdateTransactionNotesCommand(transaction.Id, Notes: null);

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transaction.Notes.Should().BeNull();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithMissingTransaction_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();

        var handler = new UpdateTransactionNotesCommandHandler(db);
        var unknownId = Guid.CreateVersion7();
        var command = new UpdateTransactionNotesCommand(unknownId, "anything");

        Result result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TransactionErrors.NotFound(unknownId));
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
