using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd.Models
{

    public sealed class RetryResult<T>
    {
        public bool IsSuccess { get; private set; }
        public T? Value { get; private set; }
        public Exception? Exception { get; private set; }
        public int Attempts { get; private set; }

        public static RetryResult<T> Success(T? value, int attempts) =>
            new RetryResult<T> { IsSuccess = true, Value = value, Attempts = attempts };

        public static RetryResult<T> Fail(T? value, Exception? exception, int attempts) =>
            new RetryResult<T> { IsSuccess = false, Value = value, Exception = exception, Attempts = attempts };
    }
}
