namespace AutoEQ.Models;

/// <summary>
/// Lightweight result type for AutoEQ business operations. Replaces silent
/// catch-and-swallow blocks with an explicit success/failure value that callers
/// can inspect, log, and surface to the user without throwing across the UI thread.
/// </summary>
public readonly struct AutoEqResult
{
    private AutoEqResult(bool success, string message, string? errorCode, Exception? exception)
    {
        Success = success;
        Message = message;
        ErrorCode = errorCode;
        Exception = exception;
    }

    public bool Success { get; }
    public bool IsFailure => !Success;
    public string Message { get; }
    public string? ErrorCode { get; }
    public Exception? Exception { get; }

    public static AutoEqResult Ok(string message = "") => new(true, message, null, null);

    public static AutoEqResult Fail(string message, string? errorCode = null, Exception? exception = null) =>
        new(false, message, errorCode, exception);

    public static AutoEqResult FromException(Exception exception, string? errorCode = null) =>
        new(false, exception.Message, errorCode, exception);
}

/// <summary>
/// Result type carrying a value on success.
/// </summary>
public readonly struct AutoEqResult<T>
{
    private AutoEqResult(bool success, T? value, string message, string? errorCode, Exception? exception)
    {
        Success = success;
        Value = value;
        Message = message;
        ErrorCode = errorCode;
        Exception = exception;
    }

    public bool Success { get; }
    public bool IsFailure => !Success;
    public T? Value { get; }
    public string Message { get; }
    public string? ErrorCode { get; }
    public Exception? Exception { get; }

    public static AutoEqResult<T> Ok(T value, string message = "") => new(true, value, message, null, null);

    public static AutoEqResult<T> Fail(string message, string? errorCode = null, Exception? exception = null) =>
        new(false, default, message, errorCode, exception);

    public static AutoEqResult<T> FromException(Exception exception, string? errorCode = null) =>
        new(false, default, exception.Message, errorCode, exception);
}
