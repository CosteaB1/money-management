using FluentAssertions;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.SavingsGoals;

public class SavingsGoalContributionTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static IDateTimeProvider Clock() => new FixedClock(FixedNow);

    [Fact]
    public void Create_PositiveAmount_Succeeds()
    {
        var goalId = Guid.CreateVersion7();

        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            goalId,
            new Money(500m, "MDL"),
            Today,
            notes: "April bonus",
            Clock());

        result.IsSuccess.Should().BeTrue();
        result.Value.GoalId.Should().Be(goalId);
        result.Value.Amount.Amount.Should().Be(500m);
        result.Value.Amount.Currency.Should().Be("MDL");
        result.Value.OccurredOn.Should().Be(Today);
        result.Value.Notes.Should().Be("April bonus");
        result.Value.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_NegativeAmount_Succeeds_WithdrawalsAreAllowed()
    {
        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(-250m, "MDL"),
            Today,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Amount.Should().Be(-250m);
    }

    [Fact]
    public void Create_ZeroAmount_ReturnsAmountMustBeNonZero()
    {
        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(0m, "MDL"),
            Today,
            notes: null,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.ContributionAmountMustBeNonZero);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("RON")]
    public void Create_NonMdlCurrency_ReturnsMdlOnly(string currency)
    {
        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(100m, currency),
            Today,
            notes: null,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.MdlOnly);
    }

    [Fact]
    public void Create_NotesOver500Chars_ReturnsContributionNotesTooLong()
    {
        string notes = new('N', 501);

        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(100m, "MDL"),
            Today,
            notes,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.ContributionNotesTooLong);
    }

    [Fact]
    public void Create_FutureOccurredOn_ReturnsContributionOccurredOnInFuture()
    {
        DateOnly future = Today.AddDays(1);

        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(100m, "MDL"),
            future,
            notes: null,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.ContributionOccurredOnInFuture);
    }

    [Fact]
    public void Create_TodayOccurredOn_IsAllowed()
    {
        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(100m, "MDL"),
            Today,
            notes: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_EmptyGoalId_ReturnsNotFound()
    {
        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.Empty,
            new Money(100m, "MDL"),
            Today,
            notes: null,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotFound(Guid.Empty));
    }

    [Fact]
    public void Create_WhitespaceNotes_NormalizedToNull()
    {
        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(100m, "MDL"),
            Today,
            notes: "   ",
            Clock());

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().BeNull();
    }

    [Fact]
    public void Create_TrimsNotes()
    {
        Result<SavingsGoalContribution> result = SavingsGoalContribution.Create(
            Guid.CreateVersion7(),
            new Money(100m, "MDL"),
            Today,
            notes: "  hello  ",
            Clock());

        result.IsSuccess.Should().BeTrue();
        result.Value.Notes.Should().Be("hello");
    }
}
