using System.Collections.ObjectModel;

namespace Nyssa.Mcp.Server.Models
{
    /// <summary>
    /// Represents the result of an operation that can succeed or fail with detailed error information
    /// </summary>
    /// <typeparam name="T">The type of the success value</typeparam>
    public class Result<T>
    {
        private readonly T? _value;
        private readonly List<ErrorMessage> _errors;

        /// <summary>
        /// The success value. Only access this if Success is true.
        /// </summary>
        public T Value
        {
            get
            {
                if (!Success)
                    throw new InvalidOperationException("Cannot access Value when Result has errors. Check Success property first.");
                return _value!;
            }
        }

        /// <summary>
        /// Collection of errors that occurred during the operation
        /// </summary>
        public ReadOnlyCollection<ErrorMessage> Errors => _errors.AsReadOnly();

        /// <summary>
        /// True if the operation succeeded (no errors), false otherwise
        /// </summary>
        public bool Success => _errors.Count == 0;

        /// <summary>
        /// True if the operation failed (has errors), false otherwise
        /// </summary>
        public bool IsFailure => !Success;

        protected Result(T value)
        {
            _value = value;
            _errors = new List<ErrorMessage>();
        }

        protected Result(IEnumerable<ErrorMessage> errors)
        {
            _value = default;
            _errors = new List<ErrorMessage>(errors);
            
            if (_errors.Count == 0)
                throw new ArgumentException("Cannot create failed Result without errors", nameof(errors));
        }

        protected Result(ErrorMessage error)
        {
            _value = default;
            _errors = new List<ErrorMessage> { error };
        }

        /// <summary>
        /// Creates a successful result with the given value
        /// </summary>
        public static Result<T> Ok(T value) => new(value);

        /// <summary>
        /// Creates a failed result with a single error
        /// </summary>
        public static Result<T> Fail(ErrorMessage error) => new(error);

        /// <summary>
        /// Creates a failed result with multiple errors
        /// </summary>
        public static Result<T> Fail(IEnumerable<ErrorMessage> errors) => new(errors);

        /// <summary>
        /// Creates a failed result with multiple errors
        /// </summary>
        public static Result<T> Fail(params ErrorMessage[] errors) => new(errors);

        /// <summary>
        /// Chains operations that return Results. Only executes if current Result is successful.
        /// Errors shortcut the chain - subsequent operations are skipped.
        /// </summary>
        public Result<TNext> Then<TNext>(Func<T, Result<TNext>> next)
        {
            if (IsFailure)
                return Result<TNext>.Fail(_errors);

            return next(Value);
        }

        /// <summary>
        /// Chains async operations that return Results. Only executes if current Result is successful.
        /// </summary>
        public async Task<Result<TNext>> ThenAsync<TNext>(Func<T, Task<Result<TNext>>> next)
        {
            if (IsFailure)
                return Result<TNext>.Fail(_errors);

            return await next(Value);
        }

        /// <summary>
        /// Transforms the success value if the Result is successful, otherwise propagates errors
        /// </summary>
        public Result<TNext> Select<TNext>(Func<T, TNext> selector)
        {
            if (IsFailure)
                return Result<TNext>.Fail(_errors);

            return Result<TNext>.Ok(selector(Value));
        }

        /// <summary>
        /// Adds additional errors to a failed result
        /// </summary>
        public Result<T> AddErrors(params ErrorMessage[] additionalErrors)
        {
            var allErrors = _errors.Concat(additionalErrors);
            return new Result<T>(allErrors);
        }

        /// <summary>
        /// Implicit conversion from T to successful Result<T>
        /// </summary>
        public static implicit operator Result<T>(T value) => Ok(value);

        /// <summary>
        /// Implicit conversion from ErrorMessage to failed Result<T>
        /// </summary>
        public static implicit operator Result<T>(ErrorMessage error) => Fail(error);

        /// <summary>
        /// Implicit conversion from ErrorMessage array to failed Result<T>
        /// </summary>
        public static implicit operator Result<T>(ErrorMessage[] errors) => Fail(errors);

        public override string ToString()
        {
            if (Success)
                return $"Success: {Value}";
            
            return $"Failure: {string.Join(", ", _errors.Select(e => e.ToString()))}";
        }
    }

    /// <summary>
    /// Represents a Unit type for operations that don't return a value but can fail
    /// </summary>
    public readonly struct Unit
    {
        public static readonly Unit Value = new();
        public override string ToString() => "()";
    }

    /// <summary>
    /// Result type for operations that don't return a value but can fail
    /// </summary>
    public class Result : Result<Unit>
    {
        protected Result(Unit value) : base(value) { }
        protected Result(IEnumerable<ErrorMessage> errors) : base(errors) { }
        protected Result(ErrorMessage error) : base(error) { }

        /// <summary>
        /// Creates a successful result for operations that don't return a value
        /// </summary>
        public new static Result Ok() => new(Unit.Value);

        /// <summary>
        /// Creates a failed result with a single error
        /// </summary>
        public new static Result Fail(ErrorMessage error) => new(error);

        /// <summary>
        /// Creates a failed result with multiple errors
        /// </summary>
        public new static Result Fail(IEnumerable<ErrorMessage> errors) => new(errors);

        /// <summary>
        /// Creates a failed result with multiple errors
        /// </summary>
        public new static Result Fail(params ErrorMessage[] errors) => new(errors);

        /// <summary>
        /// Implicit conversion from ErrorMessage to failed Result
        /// </summary>
        public static implicit operator Result(ErrorMessage error) => Fail(error);

        /// <summary>
        /// Implicit conversion from ErrorMessage array to failed Result
        /// </summary>
        public static implicit operator Result(ErrorMessage[] errors) => Fail(errors);
    }

    /// <summary>
    /// Extension methods for Result operations
    /// </summary>
    public static class ResultExtensions
    {
        /// <summary>
        /// Combines multiple Results into a single Result containing all values or all errors
        /// </summary>
        public static Result<IEnumerable<T>> Combine<T>(this IEnumerable<Result<T>> results)
        {
            var resultList = results.ToList();
            var errors = resultList.Where(r => r.IsFailure).SelectMany(r => r.Errors).ToList();
            
            if (errors.Any())
                return Result<IEnumerable<T>>.Fail(errors);
            
            var values = resultList.Select(r => r.Value);
            return Result<IEnumerable<T>>.Ok(values);
        }

        /// <summary>
        /// Returns the success value or a default value if the Result failed
        /// </summary>
        public static T GetValueOrDefault<T>(this Result<T> result, T defaultValue = default!)
        {
            return result.Success ? result.Value : defaultValue;
        }

        /// <summary>
        /// Executes an action if the Result is successful
        /// </summary>
        public static Result<T> OnSuccess<T>(this Result<T> result, Action<T> action)
        {
            if (result.Success)
                action(result.Value);
            
            return result;
        }

        /// <summary>
        /// Executes an action if the Result failed
        /// </summary>
        public static Result<T> OnFailure<T>(this Result<T> result, Action<IEnumerable<ErrorMessage>> action)
        {
            if (result.IsFailure)
                action(result.Errors);
            
            return result;
        }
    }
}