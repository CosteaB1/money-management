using FluentValidation;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools.RecordCostReimbursement;

public sealed class RecordCostReimbursementCommandValidator : AbstractValidator<RecordCostReimbursementCommand>
{
    public RecordCostReimbursementCommandValidator()
    {
        RuleFor(c => c.PoolId).NotEqual(Guid.Empty);
        RuleFor(c => c.Amount).GreaterThan(0m);

        RuleFor(c => c.PoolValueNow)
            .GreaterThan(0m)
            .When(c => c.PoolValueNow is not null);

        RuleFor(c => c.Notes)
            .MaximumLength(PoolUnitEvent.NotesMaxLength)
            .When(c => c.Notes is not null);
    }
}
