using MoneyManagement.SharedKernel;

namespace MoneyManagement.Application.Abstractions.Events;

public interface IDomainEventsDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
