// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Resilience
{
    /// <summary>
    /// Bounds for <see cref="RetryExecutor"/>'s exponential backoff. Defaults are tuned for the bridge's low
    /// volume: a few quick retries to ride out a transient blip, capped so a poll tick never stalls for long.
    /// </summary>
    public sealed class RetryOptions
    {
        /// <summary>Total attempts including the first (so 4 = 1 try + 3 retries).</summary>
        public int MaxAttempts { get; set; } = 4;

        /// <summary>Base delay; the backoff grows BaseDelay * 2^(attempt-1) before jitter.</summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>Ceiling on a single backoff delay.</summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(8);
    }
}
