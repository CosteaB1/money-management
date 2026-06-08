using FluentValidation;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Transactions.CreateTransfer;

public sealed class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(c => c.SourceAccountId).NotEmpty();
        RuleFor(c => c.DestinationAccountId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0);

        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly));

        RuleFor(c => c.Description)
            .NotEmpty()
            .MaximumLength(Transaction.DescriptionMaxLength);

        When(c => c.DestinationAmount is not null, () =>
            RuleFor(c => c.DestinationAmount!.Value).GreaterThan(0));

        RuleFor(c => c.Notes).MaximumLength(Transaction.NotesMaxLength).When(c => c.Notes is not null);
    }
}
