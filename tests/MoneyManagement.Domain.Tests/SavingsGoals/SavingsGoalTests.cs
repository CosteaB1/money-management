using FluentAssertions;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.SavingsGoals;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Domain.Tests.SavingsGoals;

public class SavingsGoalTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FutureDate = new(2026, 12, 31);

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static IDateTimeProvider Clock(DateTime? utcNow = null) =>
        new FixedClock(utcNow ?? FixedNow);

    private static Money ValidTarget(decimal amount = 10_000m) => new(amount, "MDL");

    [Fact]
    public void Create_ManualMode_StartsAtZeroMdl()
    {
        Result<SavingsGoal> result = SavingsGoal.Create(
            "Emergency Fund",
            ValidTarget(10_000m),
            FutureDate,
            linkedAccountId: null,
            Clock());

        result.IsSuccess.Should().BeTrue();
        SavingsGoal goal = result.Value;
        goal.Name.Should().Be("Emergency Fund");
        goal.TargetAmount.Amount.Should().Be(10_000m);
        goal.TargetAmount.Currency.Should().Be("MDL");
        goal.TargetDate.Should().Be(FutureDate);
        goal.LinkedAccountId.Should().BeNull();
        goal.ManualSavedAmount.Should().NotBeNull();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(0m);
        goal.ManualSavedAmount!.Value.Currency.Should().Be("MDL");
        goal.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Create_LinkedMode_LeavesManualSavedNull()
    {
        var linkedId = Guid.CreateVersion7();

        Result<SavingsGoal> result = SavingsGoal.Create(
            "XTB Snowball",
            ValidTarget(50_000m),
            FutureDate,
            linkedAccountId: linkedId,
            Clock());

        result.IsSuccess.Should().BeTrue();
        SavingsGoal goal = result.Value;
        goal.LinkedAccountId.Should().Be(linkedId);
        goal.ManualSavedAmount.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ReturnsNameRequired(string? name)
    {
        Result<SavingsGoal> result = SavingsGoal.Create(
            name!,
            ValidTarget(),
            null,
            null,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NameRequired);
    }

    [Fact]
    public void Create_WithNameOver100Chars_ReturnsNameTooLong()
    {
        string name = new('A', 101);

        Result<SavingsGoal> result = SavingsGoal.Create(name, ValidTarget(), null, null, Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NameTooLong);
    }

    [Fact]
    public void Create_TrimsName()
    {
        Result<SavingsGoal> result = SavingsGoal.Create("  Vacation  ", ValidTarget(), null, null, Clock());

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Vacation");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1_000)]
    public void Create_WithNonPositiveTarget_ReturnsTargetMustBePositive(decimal amount)
    {
        Result<SavingsGoal> result = SavingsGoal.Create(
            "Goal",
            new Money(amount, "MDL"),
            null,
            null,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.TargetMustBePositive);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("RON")]
    public void Create_WithNonMdlCurrency_ReturnsMdlOnly(string currency)
    {
        Result<SavingsGoal> result = SavingsGoal.Create(
            "Goal",
            new Money(1_000m, currency),
            null,
            null,
            Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.MdlOnly);
    }

    [Fact]
    public void Create_WithTargetDateInPast_ReturnsTargetDateInPast()
    {
        var pastDate = new DateOnly(2026, 4, 30); // FixedNow is 2026-05-01

        Result<SavingsGoal> result = SavingsGoal.Create("Goal", ValidTarget(), pastDate, null, Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.TargetDateInPast);
    }

    [Fact]
    public void Create_WithTargetDateToday_IsAllowed()
    {
        var today = DateOnly.FromDateTime(FixedNow);

        Result<SavingsGoal> result = SavingsGoal.Create("Goal", ValidTarget(), today, null, Clock());

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Rename_WithValidName_Updates()
    {
        SavingsGoal goal = SavingsGoal.Create("Old", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.Rename("Brand New");

        result.IsSuccess.Should().BeTrue();
        goal.Name.Should().Be("Brand New");
    }

    [Fact]
    public void Rename_WithBlankName_Fails()
    {
        SavingsGoal goal = SavingsGoal.Create("Old", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.Rename("   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NameRequired);
        goal.Name.Should().Be("Old");
    }

    [Fact]
    public void UpdateTarget_WithValidTarget_Updates()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(1_000m), null, null, Clock()).Value;

        Result result = goal.UpdateTarget(new Money(5_000m, "MDL"));

        result.IsSuccess.Should().BeTrue();
        goal.TargetAmount.Amount.Should().Be(5_000m);
    }

    [Fact]
    public void UpdateTarget_WithNonMdlCurrency_Fails()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.UpdateTarget(new Money(2_000m, "USD"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.MdlOnly);
        goal.TargetAmount.Amount.Should().Be(10_000m); // unchanged
    }

    [Fact]
    public void UpdateTargetDate_AllowsNullToClear()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), FutureDate, null, Clock()).Value;

        Result result = goal.UpdateTargetDate(null, Clock());

        result.IsSuccess.Should().BeTrue();
        goal.TargetDate.Should().BeNull();
    }

    [Fact]
    public void UpdateTargetDate_RejectsPastDate()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), FutureDate, null, Clock()).Value;
        var pastDate = new DateOnly(2026, 4, 30);

        Result result = goal.UpdateTargetDate(pastDate, Clock());

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.TargetDateInPast);
        goal.TargetDate.Should().Be(FutureDate);
    }

    [Fact]
    public void LinkAccount_SwitchesIntoLinkedModeAndClearsManualSaved()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;
        goal.ManualSavedAmount.Should().NotBeNull(); // manual mode initially
        var linkedId = Guid.CreateVersion7();

        Result result = goal.LinkAccount(linkedId);

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().Be(linkedId);
        goal.ManualSavedAmount.Should().BeNull();
    }

    [Fact]
    public void LinkAccount_WithEmptyAccountId_ReturnsNotFound()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.LinkAccount(Guid.Empty);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotFound(Guid.Empty));
    }

    [Fact]
    public void LinkAccount_AllowsReLinkingToDifferentAccount()
    {
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, firstId, Clock()).Value;

        Result result = goal.LinkAccount(secondId);

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().Be(secondId);
    }

    [Fact]
    public void Unlink_SwitchesIntoManualMode()
    {
        var linkedId = Guid.CreateVersion7();
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, linkedId, Clock()).Value;

        Result result = goal.Unlink();

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().BeNull();
        goal.ManualSavedAmount.Should().NotBeNull();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(0m);
        goal.ManualSavedAmount!.Value.Currency.Should().Be("MDL");
    }

    [Fact]
    public void Unlink_WhenAlreadyManual_PreservesSavedAmount()
    {
        // B-2 defense-in-depth: Unlink only resets to zero on a real
        // linked -> manual transition. An already-manual goal keeps its saved
        // progress so an accidental re-unlink can't wipe the user's data.
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;
        goal.SetManualSaved(new Money(123m, "MDL"));

        Result result = goal.Unlink();

        result.IsSuccess.Should().BeTrue();
        goal.LinkedAccountId.Should().BeNull();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(123m);
    }

    [Fact]
    public void SetManualSaved_InManualMode_Updates()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.SetManualSaved(new Money(2_500m, "MDL"));

        result.IsSuccess.Should().BeTrue();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(2_500m);
    }

    [Fact]
    public void SetManualSaved_InLinkedMode_Rejects()
    {
        var linkedId = Guid.CreateVersion7();
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, linkedId, Clock()).Value;

        Result result = goal.SetManualSaved(new Money(2_500m, "MDL"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.NotInManualMode);
        goal.ManualSavedAmount.Should().BeNull();
    }

    [Fact]
    public void SetManualSaved_NonMdlCurrency_Rejects()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.SetManualSaved(new Money(100m, "USD"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.MdlOnly);
    }

    [Fact]
    public void SetManualSaved_NegativeAmount_Rejects()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.SetManualSaved(new Money(-1m, "MDL"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SavingsGoalErrors.ManualSavedMustBeNonNegative);
    }

    [Fact]
    public void SetManualSaved_ZeroAmount_IsAllowed()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;
        goal.SetManualSaved(new Money(500m, "MDL"));

        Result result = goal.SetManualSaved(new Money(0m, "MDL"));

        result.IsSuccess.Should().BeTrue();
        goal.ManualSavedAmount!.Value.Amount.Should().Be(0m);
    }

    [Fact]
    public void Archive_SetsIsArchivedToTrue()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;

        Result result = goal.Archive();

        result.IsSuccess.Should().BeTrue();
        goal.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Archive_IsIdempotent()
    {
        SavingsGoal goal = SavingsGoal.Create("Goal", ValidTarget(), null, null, Clock()).Value;
        goal.Archive();

        Result result = goal.Archive();

        result.IsSuccess.Should().BeTrue();
        goal.IsArchived.Should().BeTrue();
    }
}
