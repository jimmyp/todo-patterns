namespace TodoList.Api.Domain;

public sealed class DomainResult<T>
{
    private DomainResult(T value)         { Value = value; Errors = []; }
    private DomainResult(string[] errors) { Value = default; Errors = errors; }

    public T? Value { get; }
    public string[] Errors { get; }
    public bool IsSuccess => Errors.Length == 0;

    public static DomainResult<T> Ok(T value)                  => new(value);
    public static DomainResult<T> Fail(params string[] errors) => new(errors);
}
