namespace Recuro.Admin.Domain.Common;

/// <summary>Kind of failure, mapped to an HTTP status in one place in the Api layer.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,

    /// <summary>A service this one depends on did not answer; the caller can retry.</summary>
    Unavailable,
}

/// <summary>An expected failure: a broken rule, a missing record or an invalid input.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Unavailable(string code, string message) => new(code, message, ErrorType.Unavailable);
}
