// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Rest;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Resilience
{
    /// <summary>
    /// Classifies a fault as transient (worth a retry) vs terminal (rethrow + log loud). The distinction is the
    /// load-bearing part of the loud-not-silent guarantee: a dead Direct Line secret (401/403) must NOT be retried
    /// into silence, while a 5xx/429/timeout should ride out a blip.
    /// </summary>
    public static class TransientFaultClassifier
    {
        /// <summary>
        /// Transient Direct Line faults: network errors, HttpClient timeouts, and HTTP 429 / 5xx. Auth/usage
        /// errors (401/403/400) and 404 (a conversation that has expired server-side) are terminal. Unknown
        /// exception types are treated as terminal so we never blindly retry something we do not understand.
        /// (Caller cancellation is handled by <see cref="RetryExecutor"/> before this runs, so a
        /// <see cref="TaskCanceledException"/> reaching here is an operation timeout, not a shutdown.)
        /// </summary>
        public static bool IsTransientDirectLineFault(Exception ex)
        {
            switch (ex)
            {
                case null:
                    return false;
                case HttpRequestException:
                    return true;
                case TaskCanceledException:
                    return true;
                case HttpOperationException httpOperationException:
                    var status = (int?)httpOperationException.Response?.StatusCode;
                    return status == null || status == 429 || status >= 500;
                default:
                    return false;
            }
        }
    }
}
