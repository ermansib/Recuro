namespace Recuro.BuildingBlocks.Domain;

/// <summary>Kind of failure, mapped to an HTTP status in one place in the web layer.</summary>
public enum ErrorType
{
    /// <summary>400: the input breaks a validation rule.</summary>
    Validation,

    /// <summary>403: the caller may not do this.</summary>
    Forbidden,

    /// <summary>404: the record does not exist for this tenant.</summary>
    NotFound,

    /// <summary>409: illegal state transition, locked record or duplicate.</summary>
    Conflict,
}

/// <summary>One invalid field, returned to the client as <c>{ field, code, message }</c>.</summary>
public sealed record FieldError(string Field, string Code, string Message);

/// <summary>An expected failure: a broken rule, a missing record or an invalid input.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>Per-field details for validation failures. Empty for other kinds.</summary>
    public IReadOnlyList<FieldError> Fields { get; init; } = [];

    /// <summary>
    /// Extra machine-readable members for the ProblemDetails body (camelCase keys), e.g. the id of the
    /// record a duplicate clashed with, so callers never parse the message.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; init; } = NoExtensions;

    // One shared instance, so errors without extensions keep comparing equal by value.
    private static readonly IReadOnlyDictionary<string, object?> NoExtensions = new Dictionary<string, object?>();

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Validation(IReadOnlyList<FieldError> fields) =>
        new("validation_failed", "One or more fields are invalid.", ErrorType.Validation) { Fields = fields };

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
}
