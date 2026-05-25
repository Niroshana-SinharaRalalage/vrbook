namespace VrBook.Domain.Common;

/// <summary>
/// Functional result type for handlers that prefer explicit failure paths over exceptions.
/// Used sparingly — exceptions are still the default for domain rule violations because
/// the ProblemDetails middleware maps them cleanly to RFC 7807 responses.
/// </summary>
public readonly record struct Result<T>(bool IsSuccess, T? Value, string? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}

public readonly record struct Result(bool IsSuccess, string? Error)
{
    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}
