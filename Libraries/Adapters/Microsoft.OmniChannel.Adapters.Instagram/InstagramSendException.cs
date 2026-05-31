// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Thrown when the Instagram Send API returns a non-success status. Carries the integer HTTP status so the
    /// outbound path can classify transient (5xx/429) vs terminal (4xx auth, e.g. a dead token = 401/190) WITHOUT
    /// parsing a message string — the loud-not-silent guarantee depends on a reliably-readable status.
    /// </summary>
    public sealed class InstagramSendException : Exception
    {
        public InstagramSendException(int statusCode, string responseBody)
            : base($"Instagram Send API returned {statusCode}: {responseBody}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        /// <summary>The HTTP status code returned by the Send API.</summary>
        public int StatusCode { get; }

        /// <summary>The (possibly empty) response body, for diagnostics.</summary>
        public string ResponseBody { get; }
    }
}
