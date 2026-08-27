using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.Loans.ArchiveLoan;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Loans;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.Loans;

public class ArchiveLoanCommandHandlerTests
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
            LoanDirection.Given,
            "Ion",
            new Money(1_000m, "MDL"),
            new DateOnly(2026, 1, 15),
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task Handle_ActiveLoan_Archives()
    {
        Loan loan = NewLoan();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new ArchiveLoanCommandHandler(db);

        Result result = await handler.Handle(new ArchiveLoanCommand(loan.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        loan.IsArchived.Should().BeTrue();
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyArchivedLoan_SucceedsIdempotently()
    {
        Loan loan = NewLoan();
        loan.Archive();
        IApplicationDbContext db = FakeApplicationDbContext.Create(loans: [loan]);
        var handler = new ArchiveLoanCommandHandler(db);

        // The handler loads with IgnoreQueryFilters, so re-archiving an
        // archived loan is a success, not a 404.
        Result result = await handler.Handle(new ArchiveLoanCommand(loan.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        loan.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnknownId_ReturnsNotFound()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new ArchiveLoanCommandHandler(db);

        var missingId = Guid.CreateVersion7();
        Result result = await handler.Handle(new ArchiveLoanCommand(missingId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LoanErrors.NotFound(missingId));
    }
}
