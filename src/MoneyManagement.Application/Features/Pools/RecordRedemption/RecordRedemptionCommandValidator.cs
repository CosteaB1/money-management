using FluentValidation;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools.RecordRedemption;

public sealed class RecordRedemptionCommandValidator : AbstractValidator<RecordRedemptionCommand>
{
    public RecordRedemptionCommandValidator()
    {
        RuleFor(c => c.PoolId).NotEqual(Guid.Empty);
        RuleFor(c => c.ParticipantId).NotEqual(Guid.Empty);
        RuleFor(c => c.PoolValueNow).GreaterThan(0m);
        RuleFor(c => c.Cash).GreaterThan(0m);

        RuleFor(c => c.DestinationAccountId)
            .NotEqual(Guid.Empty)
            .When(c => c.DestinationAccountId is not null);

        RuleFor(c => c.Notes)
            .MaximumLength(PoolUnitEvent.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
