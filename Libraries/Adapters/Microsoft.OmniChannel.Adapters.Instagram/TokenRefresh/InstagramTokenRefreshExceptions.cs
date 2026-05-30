// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// The token is expired or otherwise invalid (Graph error code 190) and cannot be auto-refreshed —
    /// a manual re-mint is required. Terminal: the refresher logs this as critical.
    /// </summary>
    public class InstagramTokenExpiredException : Exception
    {
        public InstagramTokenExpiredException(string message) : base(message)
        {
        }

        public InstagramTokenExpiredException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// The token is less than 24 hours old, so Graph refuses to refresh it yet. Transient: the refresher
    /// skips and retries on the next cycle.
    /// </summary>
    public class InstagramTokenTooFreshException : Exception
    {
        public InstagramTokenTooFreshException(string message) : base(message)
        {
        }

        public InstagramTokenTooFreshException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
