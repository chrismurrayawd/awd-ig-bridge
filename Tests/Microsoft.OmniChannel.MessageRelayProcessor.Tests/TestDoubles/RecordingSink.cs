// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles
{
    /// <summary>
    /// An <see cref="IOutboundActivitySink"/> that records delivered activities, and can be configured to throw
    /// (to exercise the deliver-first / watermark-not-advanced path).
    /// </summary>
    public sealed class RecordingSink : IOutboundActivitySink
    {
        public List<Activity> Received { get; } = new List<Activity>();

        public int Calls { get; private set; }

        /// <summary>If set, every send throws this (terminal sink failure).</summary>
        public Exception ThrowOnSend { get; set; }

        public Task SendActivitiesAsync(IList<Activity> activities, CancellationToken cancellationToken)
        {
            Calls++;
            if (ThrowOnSend != null)
            {
                return Task.FromException(ThrowOnSend);
            }

            Received.AddRange(activities);
            return Task.CompletedTask;
        }
    }
}
