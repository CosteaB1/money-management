using FluentValidation;
using MoneyManagement.Domain.Transactions;

namespace MoneyManagement.Application.Features.Pools.SettleDistribution;

public sealed class SettleDistributionCommandValidator : AbstractValidator<SettleDistributionCommand>
{
    public SettleDistributionCommandValidator()
    {
        RuleFor(c => c.PoolId).NotEqual(Guid.Empty);
        RuleFor(c => c.EventId).NotEqual(Guid.Empty);

        RuleFor(c => c.SettledOn)
            .NotEqual(default(DateOnly))
            .When(c => c.SettledOn is not null);

        // Notes feed the payment transaction's Notes; cap at the same 500 chars.
        RuleFor(c => c.Notes)
            .MaximumLength(Transaction.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
