using FluentValidation;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Transactions.AdjustBalance;

public sealed class AdjustBalanceCommandValidator : AbstractValidator<AdjustBalanceCommand>
{
    public AdjustBalanceCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();

        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly))
            // Judged in UTC; the client also submits/validates dates in UTC.
            .Must(date => date <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Adjustment date cannot be in the future.");

        // Notes feed Transaction.Description; cap at the same 500 chars.
        RuleFor(c => c.Notes)
            .MaximumLength(Transaction.DescriptionMaxLength)
            .When(c => c.Notes is not null);

        // Investment/Withdrawal carry an AMOUNT moved, which must be positive.
        // Adjustment's Value is a NEW TOTAL balance and carries no sign
        // constraint (the delta against the current balance may be negative).
        // The eligibility gate (which account types support balance changes)
        // lives in the handler where the loaded account is available.
        RuleFor(c => c.Value)
            .GreaterThan(0)
            .When(c => c.Kind != BalanceChangeKind.Adjustment)
            .WithMessage("Amount must be greater than 0.");
    }
}
