namespace Shionji.Domain.Primitives;

/// <summary>成功値またはエラー値のどちらか一方を保持する。</summary>
public readonly struct Result<TValue, TError>
{
    private readonly TValue? _value;
    private readonly TError? _error;

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"失敗した Result から値を取り出せません: {_error}");

    public TError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("成功した Result からエラーを取り出せません。");

    private Result(bool isSuccess, TValue? value, TError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public static Result<TValue, TError> Success(TValue value) => new(true, value, default);

    public static Result<TValue, TError> Failure(TError error) => new(false, default, error);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<TError, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    public Result<TOut, TError> Map<TOut>(Func<TValue, TOut> map) =>
        IsSuccess
            ? Result<TOut, TError>.Success(map(_value!))
            : Result<TOut, TError>.Failure(_error!);

    public Result<TOut, TError> Bind<TOut>(Func<TValue, Result<TOut, TError>> bind) =>
        IsSuccess ? bind(_value!) : Result<TOut, TError>.Failure(_error!);
}
