using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Loans.UpdateLoan;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

public class UpdateLoanCommandHandlerTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

    private static IDateTimeProvider Clock()
    {
        IDateTimeProvider clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(FixedNow);
        return clock;
    }

    private static Loan NewLoan()
    {
        Result<Loan> result = Loan.Create(
            LoanDirection.Received,
            "Parents",
            new Money(5_000m, "EUR"),
            new DateOnly(2026, 1, 15),
            notes: "original",
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_ValidInput_UpdatesCounterpartyAndNotes()
    {
        Loan loan = NewLoan();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new UpdateLoanCommandHandler(db);

        Result result = await handler.Handle(
            new UpdateLoanCommand(loan.Id, "Grandparents", "updated"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        loan.Counterparty.Should().Be("Grandparents");
        loan.Notes.Should().Be("updated");
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new UpdateLoanCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(
            new UpdateLoanCommand(missingId, "Someone", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotFound(missingId));
    }

    [Fact]
    public async Task Handle_ArchivedLoan_ReturnsNotFound()
    {
        Loan loan = NewLoan();
        loan.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new UpdateLoanCommandHandler(db);

        Result result = await handler.Handle(
            new UpdateLoanCommand(loan.Id, "Someone", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotFound(loan.Id));
    }

    [Fact]
    public async Task Handle_BlankCounterparty_BubblesDomainErrorWithoutSaving()
    {
        Loan loan = NewLoan();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new UpdateLoanCommandHandler(db);

        Result result = await handler.Handle(
            new UpdateLoanCommand(loan.Id, "   ", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.CounterpartyRequired);
        loan.Counterparty.Should().Be("Parents");
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
