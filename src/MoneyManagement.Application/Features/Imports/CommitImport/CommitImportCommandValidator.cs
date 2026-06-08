using FluentValidation;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Imports.CommitImport;

public sealed class CommitImportCommandValidator : AbstractValidator<CommitImportCommand>
{
    public CommitImportCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.FileName).NotEmpty().MaximumLength(260);
        RuleFor(c => c.FileHash).NotEmpty().MaximumLength(64);
        RuleFor(c => c.BankSource).IsInEnum();
        RuleFor(c => c.Transactions).NotEmpty();

        RuleForEach(c => c.Transactions).ChildRules(item =>
        {
            item.RuleFor(t => t.TransactionDate).NotEqual(default(DateOnly));
            item.RuleFor(t => t.Direction).IsInEnum();
            item.RuleFor(t => t.Amount).GreaterThan(0);
            item.RuleFor(t => t.Description).NotEmpty().MaximumLength(Transaction.DescriptionMaxLength);
            item.RuleFor(t => t.OriginalCurrency)
                .Length(3)
                .When(t => t.OriginalCurrency is not null);
            item.RuleFor(t => t.OriginalAmount)
                .GreaterThan(0)
                .When(t => t.OriginalAmount is not null);
            item.RuleFor(t => t.CounterAccountId)
                .NotEqual(Guid.Empty)
                .When(t => t.CounterAccountId is not null);
            item.RuleFor(t => t.Notes)
                .MaximumLength(Transaction.NotesMaxLength)
                .When(t => t.Notes is not null);
        });
    }
}
