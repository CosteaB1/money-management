using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using MoneyManagement.Application.Abstractions.Events;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Events;

internal sealed class DomainEventsDispatcher(IServiceProvider serviceProvider) : IDomainEventsDispatcher
{
    private static readonly ConcurrentDictionary<Type, Type> HandlerTypeCache = new();

    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (IDomainEvent domainEvent in domainEvents)
        {
            Type handlerType = HandlerTypeCache.GetOrAdd(
                domainEvent.GetType(),
                eventType => typeof(IDomainEventHandler<>).MakeGenericType(eventType));

            using IServiceScope scope = serviceProvider.CreateScope();
            IEnumerable<object?> handlers = scope.ServiceProvider.GetServices(handlerType);

            foreach (object? handler in handlers)
            {
                if (handler is null)
                {
                    continue;
                }

                // ReSharper disable once PossibleNullReferenceException
                var task = (Task)handlerType
                    .GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!
                    .Invoke(handler, [domainEvent, cancellationToken])!;

                await task;
            }
        }
    }
}
