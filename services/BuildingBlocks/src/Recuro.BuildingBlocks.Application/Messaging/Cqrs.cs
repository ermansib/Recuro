using Recuro.BuildingBlocks.Domain;

namespace Recuro.BuildingBlocks.Application.Messaging;

// Recuro's own CQRS seam (no MediatR). Handlers are resolved from DI; cross-cutting concerns are
// Scrutor decorators registered in AddRecuroApplication.

/// <summary>A use case that changes state and returns no value.</summary>
public interface ICommand;

/// <summary>A use case that changes state and returns a value.</summary>
public interface ICommand<TResponse>;

/// <summary>A use case that reads state. Never changes anything.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken ct);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct);
}

/// <summary>Reacts to a domain event inside the same transaction as the change that raised it.</summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
