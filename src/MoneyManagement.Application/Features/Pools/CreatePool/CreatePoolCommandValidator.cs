using FluentValidation;
using MoneyManagement.Domain.Pools;

namespace MoneyManagement.Application.Features.Pools.CreatePool;

public sealed class CreatePoolCommandValidator : AbstractValidator<CreatePoolCommand>
{
    public CreatePoolCommandValidator()
    {
        RuleFor(c => c.AccountId).NotEqual(Guid.Empty);

        RuleFor(c => c.Name)
            .NotNull()
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Pool name is required.")
            .MaximumLength(Pool.NameMaxLength);

        RuleFor(c => c.Currency).NotEmpty().Length(3);

        RuleFor(c => c.OwnerName)
            .NotNull()
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Owner name is required.")
            .MaximumLength(PoolParticipant.NameMaxLength);

        RuleFor(c => c.InceptionDate).NotEqual(default(DateOnly));

        RuleFor(c => c.PoolValueAtInception)
            .GreaterThan(0m)
            .When(c => c.PoolValueAtInception is not null);

        RuleFor(c => c.Notes)
            .MaximumLength(Pool.NotesMaxLength)
            .When(c => c.Notes is not null);

        RuleForEach(c => c.BackdatedSubscriptions).ChildRules(sub =>
        {
            sub.RuleFor(s => s.ParticipantName)
                .NotNull()
                .Must(name => !string.IsNullOrWhiteSpace(name))
                .WithMessage("Participant name is required.")
                .MaximumLength(PoolParticipant.NameMaxLength);

            sub.RuleFor(s => s.OccurredOn).NotEqual(default(DateOnly));
            sub.RuleFor(s => s.Cash).GreaterThan(0m);
            sub.RuleFor(s => s.PoolValuePreMoney).GreaterThan(0m);

            sub.RuleFor(s => s.Notes)
                .MaximumLength(PoolUnitEvent.NotesMaxLength)
                .When(s => s.Notes is not null);
        });
    }
}
