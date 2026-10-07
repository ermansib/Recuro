using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.BuildingBlocks.Application.Behaviors;

/// <summary>Logs each use case with its outcome and duration. Never logs the payload (it may hold PII).</summary>
internal sealed class LoggingCommandDecorator<TCommand>(
    ICommandHandler<TCommand> inner,
    ILogger<TCommand> logger) : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public async Task<Result> Handle(TCommand command, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.Handle(command, ct);
        UseCaseLog.Completed(logger, typeof(TCommand).Name, result, Stopwatch.GetElapsedTime(started));
        return result;
    }
}

/// <inheritdoc cref="LoggingCommandDecorator{TCommand}"/>
internal sealed class LoggingCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<TCommand> logger) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.Handle(command, ct);
        UseCaseLog.Completed(logger, typeof(TCommand).Name, result, Stopwatch.GetElapsedTime(started));
        return result;
    }
}

/// <inheritdoc cref="LoggingCommandDecorator{TCommand}"/>
internal sealed class LoggingQueryDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    ILogger<TQuery> logger) : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.Handle(query, ct);
        UseCaseLog.Completed(logger, typeof(TQuery).Name, result, Stopwatch.GetElapsedTime(started));
        return result;
    }
}

internal static partial class UseCaseLog
{
    public static void Completed(ILogger logger, string useCase, Result result, TimeSpan elapsed)
    {
        if (result.IsSuccess)
        {
            Succeeded(logger, useCase, elapsed.TotalMilliseconds);
        }
        else
        {
            Failed(logger, useCase, result.Error!.Code, elapsed.TotalMilliseconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{UseCase} succeeded in {ElapsedMs:0.0} ms")]
    private static partial void Succeeded(ILogger logger, string useCase, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{UseCase} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void Failed(ILogger logger, string useCase, string errorCode, double elapsedMs);
}
