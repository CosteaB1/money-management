using FluentValidation;
using MoneyManagement.Domain.Accounts;

namespace MoneyManagement.Application.Features.Accounts.UpdateAccount;

public sealed class UpdateAccountCommandValidator : AbstractValidator<UpdateAccountCommand>
{
    public UpdateAccountCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .MaximumLength(Account.NameMaxLength);

        RuleFor(c => c.Notes)
            .MaximumLength(1_000)
            .When(c => c.Notes is not null);
    }
}
