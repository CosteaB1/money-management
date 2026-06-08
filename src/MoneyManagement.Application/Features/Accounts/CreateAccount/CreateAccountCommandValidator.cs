using FluentValidation;
using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Application.Features.Accounts.CreateAccount;

public sealed class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(Account.NameMaxLength);

        RuleFor(c => c.Type)
            .IsInEnum();

        RuleFor(c => c.Currency)
            .NotEmpty()
            .Length(Account.CurrencyLength)
            .Matches("^[A-Z]{3}$")
            .WithMessage("Currency must be a 3-letter uppercase ISO code (e.g. MDL, USD, EUR, RON).");

        RuleFor(c => c.OpeningDate)
            .NotEqual(default(DateOnly));

        RuleFor(c => c.Notes)
            .MaximumLength(1_000)
            .When(c => c.Notes is not null);
    }
}
