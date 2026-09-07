using FluentValidation;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools.AddPoolParticipant;

public sealed class AddPoolParticipantCommandValidator : AbstractValidator<AddPoolParticipantCommand>
{
    public AddPoolParticipantCommandValidator()
    {
        RuleFor(c => c.PoolId).NotEqual(Guid.Empty);

        RuleFor(c => c.Name)
            .NotNull()
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Participant name is required.")
            .MaximumLength(PoolParticipant.NameMaxLength);

        RuleFor(c => c.JoinedOn).NotEqual(default(DateOnly));
    }
}
