using FluentValidation;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools.CloseDistribution;

public sealed class CloseDistributionCommandValidator : AbstractValidator<CloseDistributionCommand>
{
    public CloseDistributionCommandValidator()
    {
        RuleFor(c => c.PoolId).NotEqual(Guid.Empty);
        RuleFor(c => c.PoolValueNow).GreaterThan(0m);

        RuleFor(c => c.Notes)
            .MaximumLength(PoolUnitEvent.NotesMaxLength)
            .When(c => c.Notes is not null);

        RuleForEach(c => c.Payouts).ChildRules(payout =>
        {
            payout.RuleFor(p => p.ParticipantId).NotEqual(Guid.Empty);

            // Null means "their whole distributable"; an explicit figure must be
            // a real amount. The `<= distributable` half of the rule needs the
            // register, so it lives in the handler.
            payout.RuleFor(p => p.Cash)
                .GreaterThan(0m)
                .When(p => p.Cash is not null);
        });
    }
}
