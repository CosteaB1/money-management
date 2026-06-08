using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MoneyManagement.Application.Abstractions.Events;
using MoneyManagement.Infrastructure.Events;
using MoneyManagement.SharedKernel;

namespace MoneyManagement.Infrastructure.Tests.Events;

/// <summary>
/// The dispatcher resolves <c>IDomainEventHandler&lt;TEvent&gt;</c> per event
/// type from a fresh DI scope and invokes <c>Handle</c>. Pure DI/reflection
/// wiring — exercised with a real <see cref="ServiceCollection"/> and spy
/// handlers, no DB.
/// </summary>
public sealed class DomainEventsDispatcherTests
{
    private sealed record EventA(int Value) : IDomainEvent;

    private sealed record EventB : IDomainEvent;

    private sealed record Unhandled : IDomainEvent;

    private sealed class HandlerA(List<string> log) : IDomainEventHandler<EventA>
    {
        public Task Handle(EventA domainEvent, CancellationToken cancellationToken)
        {
            log.Add($"A:{domainEvent.Value}");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondHandlerA(List<string> log) : IDomainEventHandler<EventA>
    {
        public Task Handle(EventA domainEvent, CancellationToken cancellationToken)
        {
            log.Add($"A2:{domainEvent.Value}");
            return Task.CompletedTask;
        }
    }

    private sealed class HandlerB(List<string> log) : IDomainEventHandler<EventB>
    {
        public Task Handle(EventB domainEvent, CancellationToken cancellationToken)
        {
            log.Add("B");
            return Task.CompletedTask;
        }
    }

    private static (DomainEventsDispatcher Dispatcher, List<string> Log) Build(
        Action<IServiceCollection> register)
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        register(services);
        ServiceProvider provider = services.BuildServiceProvider();
        return (new DomainEventsDispatcher(provider), log);
    }

    [Fact]
    public async Task Dispatch_InvokesMatchingHandler()
    {
        (DomainEventsDispatcher dispatcher, List<string> log) = Build(s =>
            s.AddScoped<IDomainEventHandler<EventA>, HandlerA>());

        await dispatcher.DispatchAsync([new EventA(42)]);

        log.Should().ContainSingle().Which.Should().Be("A:42");
    }

    [Fact]
    public async Task Dispatch_InvokesAllHandlersForOneEvent()
    {
        (DomainEventsDispatcher dispatcher, List<string> log) = Build(s =>
        {
            s.AddScoped<IDomainEventHandler<EventA>, HandlerA>();
            s.AddScoped<IDomainEventHandler<EventA>, SecondHandlerA>();
        });

        await dispatcher.DispatchAsync([new EventA(7)]);

        log.Should().BeEquivalentTo(["A:7", "A2:7"]);
    }

    [Fact]
    public async Task Dispatch_RoutesEachEventToItsOwnHandlerType()
    {
        (DomainEventsDispatcher dispatcher, List<string> log) = Build(s =>
        {
            s.AddScoped<IDomainEventHandler<EventA>, HandlerA>();
            s.AddScoped<IDomainEventHandler<EventB>, HandlerB>();
        });

        await dispatcher.DispatchAsync([new EventA(1), new EventB()]);

        log.Should().Equal("A:1", "B");
    }

    [Fact]
    public async Task Dispatch_EventWithNoHandler_NoThrow()
    {
        (DomainEventsDispatcher dispatcher, List<string> log) = Build(_ => { });

        Func<Task> act = () => dispatcher.DispatchAsync([new Unhandled()]);

        await act.Should().NotThrowAsync();
        log.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_SkipsNullHandlerEntries_StillInvokesRealOnes()
    {
        // A registration whose factory yields null produces a null entry in
        // GetServices(handlerType); the dispatcher must skip it (the `handler is
        // null` continue) and still invoke the real handler registered after it.
        (DomainEventsDispatcher dispatcher, List<string> log) = Build(s =>
        {
            s.AddScoped(typeof(IDomainEventHandler<EventA>), _ => null!);
            s.AddScoped<IDomainEventHandler<EventA>, HandlerA>();
        });

        await dispatcher.DispatchAsync([new EventA(99)]);

        log.Should().ContainSingle().Which.Should().Be("A:99");
    }

    [Fact]
    public async Task Dispatch_EmptyList_NoThrow_NoHandlers()
    {
        (DomainEventsDispatcher dispatcher, List<string> log) = Build(s =>
            s.AddScoped<IDomainEventHandler<EventA>, HandlerA>());

        await dispatcher.DispatchAsync([]);

        log.Should().BeEmpty();
    }
}
