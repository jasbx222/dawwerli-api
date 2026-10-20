namespace Dawwerli.Domain.Common;

public enum ErrorKind { Validation, Unauthorized, Forbidden, NotFound, Conflict, PaymentRequired, BusinessRule, RateLimited }

/// <summary>Business error carried by <see cref="Result"/>; never thrown.</summary>
public sealed record Error(ErrorKind Kind, string Code, string Message, IReadOnlyDictionary<string, object?>? Data = null)
{
    public static Error Validation(string code, string message) => new(ErrorKind.Validation, code, message);
    public static Error NotFound(string what) => new(ErrorKind.NotFound, $"{what}.not_found", $"{what} not found");
    public static Error Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);
    public static Error Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);
    public static Error Rule(string code, string message) => new(ErrorKind.BusinessRule, code, message);
    public static Error Payment(string code, string message) => new(ErrorKind.PaymentRequired, code, message);
    public static Error Unauthorized(string code, string message) => new(ErrorKind.Unauthorized, code, message);
    public static Error Limited(string code, string message, int retryInSeconds) =>
        new(ErrorKind.RateLimited, code, message, new Dictionary<string, object?> { ["retryIn"] = retryInSeconds });
}

public class Result
{
    protected Result(Error? error) => Error = error;
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public static Result Ok() => new(null);
    public static Result Fail(Error error) => new(error);
    public static implicit operator Result(Error error) => new(error);
}

public sealed class Result<T> : Result
{
    private Result(T? value, Error? error) : base(error) => Value = value;
    public T? Value { get; }
    public static Result<T> Ok(T value) => new(value, null);
    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(Error error) => new(default, error);
}
