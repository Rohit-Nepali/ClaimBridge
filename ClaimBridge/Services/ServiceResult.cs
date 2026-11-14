namespace ClaimBridge.Services;

public enum ServiceStatus
{
    Success,
    Invalid,
    Unauthorized,
    NotFound
}

public sealed class ServiceResult<T>
{
    public ServiceStatus Status { get; private init; }

    public T? Value { get; private init; }

    public Dictionary<string, string[]>? Errors { get; private init; }

    public string? Error { get; private init; }

    public bool Succeeded => Status == ServiceStatus.Success;

    public static ServiceResult<T> Success(T value) =>
        new() { Status = ServiceStatus.Success, Value = value };

    public static ServiceResult<T> Invalid(Dictionary<string, string[]> errors) =>
        new() { Status = ServiceStatus.Invalid, Errors = errors };

    public static ServiceResult<T> Unauthorized(string error) =>
        new() { Status = ServiceStatus.Unauthorized, Error = error };

    public static ServiceResult<T> NotFound(string error) =>
        new() { Status = ServiceStatus.NotFound, Error = error };
}
