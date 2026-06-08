using FluentValidation;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Transactions.UpdateTransactionNotes;

public sealed class UpdateTransactionNotesCommandValidator : AbstractValidator<UpdateTransactionNotesCommand>
{
    public UpdateTransactionNotesCommandValidator()
    {
        RuleFor(c => c.Id)
            .NotEmpty();

        RuleFor(c => c.Notes)
            .MaximumLength(Transaction.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
