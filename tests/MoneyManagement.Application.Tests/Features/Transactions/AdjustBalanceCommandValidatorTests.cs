using FluentValidation.TestHelper;
using MoneyManagement.Application.Features.Transactions.AdjustBalance;

namespace MoneyManagement.Application.Tests.Features.Transactions;

public class AdjustBalanceCommandValidatorTests
{
    private static readonly DateOnly ChangeDate = new(2026, 4, 30);

    private readonly AdjustBalanceCommandValidator _validator = new();

    [Theory]
    [InlineData(BalanceChangeKind.Investment)]
    [InlineData(BalanceChangeKind.Withdrawal)]
    public void Validate_ZeroValue_RejectedForInvestmentAndWithdrawal(BalanceChangeKind kind)
    {
        var command = new AdjustBalanceCommand(Guid.NewGuid(), kind, Value: 0m, ChangeDate, Notes: null);

        TestValidationResult<AdjustBalanceCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Value)
            .WithErrorMessage("Amount must be greater than 0.");
    }

    [Theory]
    [InlineData(BalanceChangeKind.Investment)]
    [InlineData(BalanceChangeKind.Withdrawal)]
    public void Validate_NegativeValue_RejectedForInvestmentAndWithdrawal(BalanceChangeKind kind)
    {
        var command = new AdjustBalanceCommand(Guid.NewGuid(), kind, Value: -50m, ChangeDate, Notes: null);

        TestValidationResult<AdjustBalanceCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Value);
    }

    [Theory]
    [InlineData(BalanceChangeKind.Investment)]
    [InlineData(BalanceChangeKind.Withdrawal)]
    public void Validate_PositiveValue_AcceptedForInvestmentAndWithdrawal(BalanceChangeKind kind)
    {
        var command = new AdjustBalanceCommand(Guid.NewGuid(), kind, Value: 100m, ChangeDate, Notes: null);

        TestValidationResult<AdjustBalanceCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(c => c.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void Validate_AdjustmentValue_HasNoSignConstraint(decimal value)
    {
        // Adjustment's Value is a NEW TOTAL balance; any sign is valid input
        // (the handler decides whether the resulting delta is meaningful).
        var command = new AdjustBalanceCommand(
            Guid.NewGuid(),
            BalanceChangeKind.Adjustment,
            value,
            ChangeDate,
            Notes: null);

        TestValidationResult<AdjustBalanceCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(c => c.Value);
    }

    [Fact]
    public void Validate_FutureDate_Rejected()
    {
        // Future is judged in UTC. +2 days is unambiguously future.
        DateOnly future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        var command = new AdjustBalanceCommand(
            Guid.NewGuid(),
            BalanceChangeKind.Adjustment,
            Value: 100m,
            future,
            Notes: null);

        TestValidationResult<AdjustBalanceCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Date);
    }

    [Fact]
    public void Validate_TodayDate_Accepted()
    {
        // Today's UTC calendar date must pass.
        var command = new AdjustBalanceCommand(
            Guid.NewGuid(),
            BalanceChangeKind.Adjustment,
            Value: 100m,
            DateOnly.FromDateTime(DateTime.UtcNow),
            Notes: null);

        TestValidationResult<AdjustBalanceCommand> result = _validator.TestValidate(command);

        result.ShouldNotHaveValidationErrorFor(c => c.Date);
    }

    [Fact]
    public void Validate_EmptyAccountId_Rejected()
    {
        var command = new AdjustBalanceCommand(
            Guid.Empty,
            BalanceChangeKind.Adjustment,
            Value: 100m,
            ChangeDate,
            Notes: null);

        TestValidationResult<AdjustBalanceCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.AccountId);
    }
}
