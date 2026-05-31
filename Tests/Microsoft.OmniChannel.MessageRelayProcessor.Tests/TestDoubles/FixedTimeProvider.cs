// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles
{
    /// <summary>
    /// A <see cref="TimeProvider"/> returning a fixed UTC time that tests can advance, so time-dependent logic
    /// (stale sweep, lease expiry) is deterministic without sleeping. Mirrors the P1 token tests' fixed clock.
    /// </summary>
    public sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;

        public void Set(DateTimeOffset now) => _now = now;
    }
}
