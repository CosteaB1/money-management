using FluentValidation;
using FluentValidation.Results;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Abstractions.Behaviors;

internal static class ValidationDecorator
{
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            Error? validationFailure = await ValidateAsync(command, validators, cancellationToken);
            return validationFailure is not null
                ? Result.Failure(validationFailure)
                : await inner.Handle(command, cancellationToken);
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            Error? validationFailure = await ValidateAsync(command, validators, cancellationToken);
            return validationFailure is not null
                ? Result.Failure<TResponse>(validationFailure)
                : await inner.Handle(command, cancellationToken);
        }
    }

    private static async Task<Error?> ValidateAsync<TCommand>(
        TCommand command,
        IEnumerable<IValidator<TCommand>> validators,
        CancellationToken cancellationToken)
    {
        var validatorList = validators.ToList();
        if (validatorList.Count == 0)
        {
            return null;
        }

        var context = new ValidationContext<TCommand>(command);
        ValidationResult[] results = await Task.WhenAll(validatorList.Select(v => v.ValidateAsync(context, cancellationToken)));

        ValidationFailure? firstFailure = results
            .SelectMany(r => r.Errors)
            .FirstOrDefault(e => e is not null);

        return firstFailure is null
            ? null
            : Error.Validation(firstFailure.PropertyName, firstFailure.ErrorMessage);
    }
}
