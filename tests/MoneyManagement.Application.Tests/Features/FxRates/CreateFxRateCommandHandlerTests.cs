using FluentAssertions;
using MoneyManagement.Application.Abstractions.Data;
using MoneyManagement.Application.Features.FxRates.CreateFxRate;
using MoneyManagement.Application.Tests.TestSupport;
using MoneyManagement.Domain.FxRates;
using MoneyManagement.SharedKernel;
using NSubstitute;

namespace MoneyManagement.Application.Tests.Features.FxRates;

public class CreateFxRateCommandHandlerTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 2);

    [Fact]
    public async Task Handle_WithValidCommand_PersistsRateAndReturnsId()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateFxRateCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateFxRateCommand("USD", "MDL", 17.50m, AsOf), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        db.FxRates.Should().ContainSingle()
            .Which.Source.Should().Be(FxRateSource.Manual);
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNonPositiveRate_ReturnsDomainFailure_AndDoesNotSave()
    {
        // The validator shadows this in the pipeline; the handler's own domain
        // guard (FxRate.Create) must still reject a non-positive rate.
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateFxRateCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateFxRateCommand("USD", "MDL", 0m, AsOf), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FxRateErrors.RateMustBePositive);
        db.FxRates.Should().BeEmpty();
        await db.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithSameSourceAndTarget_ReturnsDomainFailure()
    {
        IApplicationDbContext db = FakeApplicationDbContext.Create();
        var handler = new CreateFxRateCommandHandler(db);

        Result<Guid> result = await handler.Handle(
            new CreateFxRateCommand("USD", "USD", 1m, AsOf), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FxRateErrors.SameSourceAndTargetCurrency);
    }
}
