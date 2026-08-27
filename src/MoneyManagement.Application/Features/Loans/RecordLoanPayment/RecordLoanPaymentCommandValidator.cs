using FluentValidation;
using MoneyManagement.Domain.Loans;

namespace MoneyManagement.Application.Features.Loans.RecordLoanPayment;

public sealed class RecordLoanPaymentCommandValidator : AbstractValidator<RecordLoanPaymentCommand>
{
    public RecordLoanPaymentCommandValidator()
    {
        RuleFor(c => c.LoanId).NotEqual(Guid.Empty);

        RuleFor(c => c.Amount).GreaterThan(0m);

        RuleFor(c => c.OccurredOn)
            .NotEqual(default(DateOnly));

        RuleFor(c => c.AccountId)
            .NotEqual(Guid.Empty)
            .When(c => c.AccountId is not null);

        RuleFor(c => c.Notes)
            .MaximumLength(LoanPayment.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
