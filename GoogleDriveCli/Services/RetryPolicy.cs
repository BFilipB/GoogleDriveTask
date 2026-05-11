using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace GoogleDriveCli.Services
{
    public class RetryPolicyResult<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public Exception? LastException { get; set; }
        public int Attempts { get; set; }
    }

    public class RetryPolicy
    {
        private readonly int _maxAttempts;
        private readonly TimeSpan _initialDelay;
        private readonly double _backoffMultiplier;
        private readonly TimeSpan _maxDelay;
        private readonly HashSet<HttpStatusCode> _retryableStatusCodes;
        private readonly HashSet<Type> _retryableExceptionTypes;

        public RetryPolicy(
            int maxAttempts = 5,
            int initialDelayMs = 100,
            double backoffMultiplier = 2.0,
            int maxDelayMs = 30000)
        {
            _maxAttempts = maxAttempts;
            _initialDelay = TimeSpan.FromMilliseconds(initialDelayMs);
            _backoffMultiplier = backoffMultiplier;
            _maxDelay = TimeSpan.FromMilliseconds(maxDelayMs);

            // HTTP 429 (Too Many Requests), 500, 502, 503, 504
            _retryableStatusCodes = new HashSet<HttpStatusCode>
            {
                HttpStatusCode.TooManyRequests,
                HttpStatusCode.InternalServerError,
                HttpStatusCode.BadGateway,
                HttpStatusCode.ServiceUnavailable,
                HttpStatusCode.GatewayTimeout,
                HttpStatusCode.RequestTimeout
            };

            _retryableExceptionTypes = new HashSet<Type>
            {
                typeof(HttpRequestException),
                typeof(TaskCanceledException),
                typeof(TimeoutException),
                typeof(IOException)
            };
        }

        /// <summary>
        /// Executes an async function with exponential backoff retry logic.
        /// </summary>
        public async Task<RetryPolicyResult<T>> ExecuteAsync<T>(
            Func<Task<T>> operation,
            string operationName = "Operation")
        {
            var result = new RetryPolicyResult<T> { Attempts = 0 };
            var delay = _initialDelay;

            for (int attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                result.Attempts = attempt;

                try
                {
                    result.Data = await operation();
                    result.Success = true;
                    return result;
                }
                catch (Exception ex)
                {
                    result.LastException = ex;

                    if (attempt == _maxAttempts)
                    {
                        result.Success = false;
                        return result;
                    }

                    // Check if this is a retryable exception
                    if (!IsRetryableException(ex))
                    {
                        result.Success = false;
                        return result;
                    }

                    // Calculate next delay with exponential backoff
                    var nextDelay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * _backoffMultiplier, _maxDelay.TotalMilliseconds));

                    await Task.Delay(delay);
                    delay = nextDelay;
                }
            }

            result.Success = false;
            return result;
        }

        /// <summary>
        /// Executes an async function that returns a result with status code, using retry logic.
        /// </summary>
        public async Task<RetryPolicyResult<T>> ExecuteWithStatusAsync<T>(
            Func<Task<(T Data, HttpStatusCode StatusCode)>> operation,
            string operationName = "Operation")
        {
            var result = new RetryPolicyResult<T> { Attempts = 0 };
            var delay = _initialDelay;

            for (int attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                result.Attempts = attempt;

                try
                {
                    var (data, statusCode) = await operation();

                    if (_retryableStatusCodes.Contains(statusCode))
                    {
                        result.LastException = new HttpRequestException($"HTTP {statusCode}");

                        if (attempt == _maxAttempts)
                        {
                            result.Success = false;
                            return result;
                        }

                        // For rate limiting (429), use longer delays
                        var nextDelay = statusCode == HttpStatusCode.TooManyRequests
                            ? TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * _backoffMultiplier * 2, 60))
                            : TimeSpan.FromMilliseconds(
                                Math.Min(delay.TotalMilliseconds * _backoffMultiplier, _maxDelay.TotalMilliseconds));

                        await Task.Delay(delay);
                        delay = nextDelay;
                    }
                    else
                    {
                        result.Data = data;
                        result.Success = true;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    result.LastException = ex;

                    if (attempt == _maxAttempts)
                    {
                        result.Success = false;
                        return result;
                    }

                    // Check if this is a retryable exception
                    if (!IsRetryableException(ex))
                    {
                        result.Success = false;
                        return result;
                    }

                    // Calculate next delay with exponential backoff
                    var nextDelay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * _backoffMultiplier, _maxDelay.TotalMilliseconds));

                    await Task.Delay(delay);
                    delay = nextDelay;
                }
            }

            result.Success = false;
            return result;
        }

        private bool IsRetryableException(Exception ex)
        {
            // Check if exception type is retryable
            if (_retryableExceptionTypes.Contains(ex.GetType()))
                return true;

            // Check for specific Google API exceptions
            var exceptionName = ex.GetType().Name;
            if (exceptionName.Contains("GoogleApiRequestException") || exceptionName.Contains("ServiceNotAvailableException"))
                return true;

            // Check inner exceptions
            if (ex.InnerException != null)
                return IsRetryableException(ex.InnerException);

            return false;
        }
    }
}
