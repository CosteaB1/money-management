using Microsoft.Extensions.Logging;
using MoneyManagement.Application.Abstractions.Messaging;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Abstractions.Behaviors;

internal static class LoggingDecorator
{
    internal sealed class CommandHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        ILogger<CommandHandler<TCommand>> logger) : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            string name = typeof(TCommand).Name;
            logger.LogInformation("Handling command {Command}", name);
            Result result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess)
            {
                logger.LogInformation("Command {Command} handled successfully", name);
            }
            else
            {
                logger.LogWarning("Command {Command} failed with error {Error}", name, result.Error);
            }

            return result;
        }
    }

    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<CommandHandler<TCommand, TResponse>> logger) : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            string name = typeof(TCommand).Name;
            logger.LogInformation("Handling command {Command}", name);
            Result<TResponse> result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess)
            {
                logger.LogInformation("Command {Command} handled successfully", name);
            }
            else
            {
                logger.LogWarning("Command {Command} failed with error {Error}", name, result.Error);
            }

            return result;
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<QueryHandler<TQuery, TResponse>> logger) : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            string name = typeof(TQuery).Name;
            logger.LogInformation("Handling query {Query}", name);
            Result<TResponse> result = await inner.Handle(query, cancellationToken);

            if (result.IsFailure)
            {
                logger.LogWarning("Query {Query} failed with error {Error}", name, result.Error);
            }

            return result;
        }
    }
}
