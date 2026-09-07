using FluentValidation;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools.RecordSubscription;

public sealed class RecordSubscriptionCommandValidator : AbstractValidator<RecordSubscriptionCommand>
{
    public RecordSubscriptionCommandValidator()
    {
        RuleFor(c => c.PoolId).NotEqual(Guid.Empty);
        RuleFor(c => c.ParticipantId).NotEqual(Guid.Empty);

        // The pre-money total is mandatory and must be real: a pool worth
        // nothing has no NAV to strike against.
        RuleFor(c => c.PoolValueNow).GreaterThan(0m);

        RuleFor(c => c.Cash).GreaterThan(0m);

        RuleFor(c => c.Notes)
            .MaximumLength(PoolUnitEvent.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
