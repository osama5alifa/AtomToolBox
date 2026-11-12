namespace Atom.Toolbox.Domain.Common;

/// <summary>
/// Outcome of an operation that can fail without throwing. Commands use this instead of
/// exceptions for expected failures (access denied, service stopped, validation).
/// </summary>
public sealed record OperationResult(bool Ok, string? Error = null)
{
    public static OperationResult Success { get; } = new(true);
    public static OperationResult Fail(string error) => new(false, error);

    public static OperationResult From(bool ok, string? error) => ok ? Success : Fail(error ?? "Operation failed");
}

public sealed record OperationResult<T>(bool Ok, T? Value, string? Error = null)
{
    public static OperationResult<T> Success(T value) => new(true, value);
    public static OperationResult<T> Fail(string error) => new(false, default, error);
}

/// <summary>Raised when a catalog entry is structurally invalid. Indicates a build defect, not user input.</summary>
public sealed class CatalogIntegrityException(string message) : Exception(message);
