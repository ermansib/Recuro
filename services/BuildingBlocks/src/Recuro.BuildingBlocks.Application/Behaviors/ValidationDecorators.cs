using FluentValidation;
using FluentValidation.Results;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.BuildingBlocks.Application.Behaviors;

/// <summary>Runs every FluentValidation validator for the command before the real handler.</summary>
internal sealed class ValidationCommandDecorator<TCommand>(
    ICommandHandler<TCommand> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public async Task<Result> Handle(TCommand command, CancellationToken ct)
    {
        var error = await Validation.RunAsync(command, validators, ct);
        return error is null ? await inner.Handle(command, ct) : error;
    }
}

/// <inheritdoc cref="ValidationCommandDecorator{TCommand}"/>
internal sealed class ValidationCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken ct)
    {
        var error = await Validation.RunAsync(command, validators, ct);
        return error is null ? await inner.Handle(command, ct) : error;
    }
}

/// <summary>Validates queries too, so filters and page sizes are checked the same way.</summary>
internal sealed class ValidationQueryDecorator<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    IEnumerable<IValidator<TQuery>> validators) : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken ct)
    {
        var error = await Validation.RunAsync(query, validators, ct);
        return error is null ? await inner.Handle(query, ct) : error;
    }
}

internal static class Validation
{
    public static async Task<Error?> RunAsync<T>(T request, IEnumerable<IValidator<T>> validators, CancellationToken ct)
    {
        var context = new ValidationContext<T>(request);
        var results = new List<ValidationResult>();
        foreach (var validator in validators)
        {
            results.Add(await validator.ValidateAsync(context, ct));
        }

        var fields = results
            .SelectMany(r => r.Errors)
            .Select(f => new FieldError(ToCamelCase(f.PropertyName), f.ErrorCode, f.ErrorMessage))
            .ToList();

        return fields.Count == 0 ? null : Error.Validation(fields);
    }

    // DTOs are camelCase on the wire (like the frontend mocks), so field names in errors are too.
    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
