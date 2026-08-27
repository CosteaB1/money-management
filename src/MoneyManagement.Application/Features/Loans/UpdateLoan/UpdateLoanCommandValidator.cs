using FluentValidation;
using MoneyManagement.Domain.Loans;

namespace MoneyManagement.Application.Features.Loans.UpdateLoan;

public sealed class UpdateLoanCommandValidator : AbstractValidator<UpdateLoanCommand>
{
    public UpdateLoanCommandValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);

        RuleFor(c => c.Counterparty)
            .NotNull()
            .Must(counterparty => !string.IsNullOrWhiteSpace(counterparty))
            .WithMessage("Counterparty is required.")
            .MaximumLength(Loan.CounterpartyMaxLength);

        RuleFor(c => c.Notes)
            .MaximumLength(Loan.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
