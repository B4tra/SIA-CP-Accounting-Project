namespace SIA.Application.Common.Models;

/// <summary>
/// Hasil operasi service tanpa nilai kembali. Pesan error ditulis dalam Bahasa Indonesia
/// agar dapat langsung ditampilkan di UI.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<string> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary>Daftar pesan error; kosong jika sukses.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Pesan error pertama, atau null jika sukses.</summary>
    public string? Error => Errors.Count > 0 ? Errors[0] : null;

    public static Result Success() => new(true, Array.Empty<string>());

    public static Result Failure(string error) => new(false, new[] { error });

    public static Result Failure(IEnumerable<string> errors)
    {
        var list = errors.ToArray();
        if (list.Length == 0)
            throw new ArgumentException("Minimal satu pesan error harus diberikan.", nameof(errors));
        return new Result(false, list);
    }
}

/// <summary>
/// Hasil operasi service dengan nilai kembali bertipe <typeparamref name="T"/>.
/// </summary>
public class Result<T> : Result
{
    private Result(bool isSuccess, T? value, IReadOnlyList<string> errors)
        : base(isSuccess, errors)
    {
        Value = value;
    }

    /// <summary>Nilai hasil; hanya bermakna jika <see cref="Result.IsSuccess"/> bernilai true.</summary>
    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, Array.Empty<string>());

    public static new Result<T> Failure(string error) => new(false, default, new[] { error });

    public static new Result<T> Failure(IEnumerable<string> errors)
    {
        var list = errors.ToArray();
        if (list.Length == 0)
            throw new ArgumentException("Minimal satu pesan error harus diberikan.", nameof(errors));
        return new Result<T>(false, default, list);
    }
}
