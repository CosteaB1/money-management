using FluentValidation;
using MoneyManagement.Domain.Loans;

namespace MoneyManagement.Application.Features.Loans.CreateLoan;

public sealed class CreateLoanCommandValidator : AbstractValidator<CreateLoanCommand>
{
    public CreateLoanCommandValidator()
    {
        RuleFor(c => c.Direction).IsInEnum();

        RuleFor(c => c.Counterparty)
            .NotNull()
            .Must(counterparty => !string.IsNullOrWhiteSpace(counterparty))
            .WithMessage("Counterparty is required.")
            .MaximumLength(Loan.CounterpartyMaxLength);

        RuleFor(c => c.Principal).GreaterThan(0m);

        RuleFor(c => c.Currency)
            .NotEmpty()
            .Matches("^[A-Z]{3}$")
            .WithMessage("Currency must be a 3-letter uppercase ISO code (e.g. MDL, USD, EUR, RON).");

        RuleFor(c => c.LoanDate)
            .NotEqual(default(DateOnly));

        RuleFor(c => c.AccountId)
            .NotEqual(Guid.Empty)
            .When(c => c.AccountId is not null);

        RuleFor(c => c.Notes)
            .MaximumLength(Loan.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
