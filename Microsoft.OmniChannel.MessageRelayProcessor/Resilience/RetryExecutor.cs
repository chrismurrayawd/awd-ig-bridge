// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Resilience
{
    /// <summary>
    /// A small hand-rolled bounded exponential-backoff-with-jitter retry helper (no Polly — matches the project's
    /// existing hand-rolled resilience in the P1 token refresher and keeps the dependency surface minimal). The
    /// caller supplies an <c>isTransient</c> predicate so the same executor classifies Direct Line and Instagram
    /// faults at their own call sites. Terminal faults are NOT retried — they rethrow so the caller can log loudly
    /// (the loud-not-silent guarantee). Caller cancellation is never treated as a fault.
    /// The delay and jitter are injectable so tests run with no real waiting and deterministic backoff.
    /// </summary>
    public sealed class RetryExecutor
    {
        private readonly RetryOptions _options;
        private readonly ILogger _logger;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Func<double> _jitter;

        public RetryExecutor(
            RetryOptions options = null,
            ILogger logger = null,
            Func<TimeSpan, CancellationToken, Task> delay = null,
            Func<double> jitter = null)
        {
            _options = options ?? new RetryOptions();
            _logger = logger ?? NullLogger.Instance;
            _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
            _jitter = jitter ?? Random.Shared.NextDouble;
        }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            Func<Exception, bool> isTransient,
            string operationName,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var attempt = 0;
            while (true)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Shutdown / caller cancellation — not a fault, never retried.
                    throw;
                }
                catch (Exception ex)
                {
                    if (isTransient != null && !isTransient(ex))
                    {
                        _logger.LogWarning(ex, "{Operation} failed with a non-transient error on attempt {Attempt}; not retrying.", operationName, attempt);
                        throw;
                    }

                    if (attempt >= _options.MaxAttempts)
                    {
                        _logger.LogWarning(ex, "{Operation} still failing after {Attempts} attempt(s); giving up this cycle.", operationName, attempt);
                        throw;
                    }

                    var delay = ComputeDelay(attempt);
                    _logger.LogWarning(ex, "{Operation} transient failure on attempt {Attempt}/{Max}; retrying in {DelayMs}ms.", operationName, attempt, _options.MaxAttempts, (int)delay.TotalMilliseconds);
                    await _delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            Func<Exception, bool> isTransient,
            string operationName,
            CancellationToken cancellationToken) =>
            ExecuteAsync(
                async c =>
                {
                    await operation(c).ConfigureAwait(false);
                    return true;
                },
                isTransient,
                operationName,
                cancellationToken);

        private TimeSpan ComputeDelay(int attempt)
        {
            // Exponential base * 2^(attempt-1), capped at MaxDelay, then FULL jitter in [0, capped].
            var exponential = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            var capped = Math.Min(exponential, _options.MaxDelay.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(capped * _jitter());
        }
    }
}
