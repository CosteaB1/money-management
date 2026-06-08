using FluentValidation;
using MoneyManagement.Domain.Common;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Transactions.CreateTransaction;

public sealed class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();

        RuleFor(c => c.TransactionDate)
            .NotEqual(default(DateOnly));

        RuleFor(c => c.Direction).IsInEnum();

        RuleFor(c => c.Amount).GreaterThan(0);

        RuleFor(c => c.Description)
            .NotEmpty()
            .MaximumLength(Transaction.DescriptionMaxLength);

        RuleFor(c => c.Notes)
            .MaximumLength(Transaction.NotesMaxLength)
            .When(c => c.Notes is not null);

        RuleFor(c => c.OriginalCurrency)
            .Length(3)
            .When(c => c.OriginalCurrency is not null);

        RuleFor(c => c.OriginalAmount)
            .GreaterThan(0)
            .When(c => c.OriginalAmount is not null);

        RuleFor(c => c.CounterAccountId)
            .NotEqual(Guid.Empty)
            .When(c => c.CounterAccountId is not null);

        RuleFor(c => c.Currency!)
            .Must(CurrencyCodes.IsValidIso)
            .WithMessage("Currency must be a 3-letter uppercase ISO code.")
            .When(c => c.Currency is not null);
    }
}
