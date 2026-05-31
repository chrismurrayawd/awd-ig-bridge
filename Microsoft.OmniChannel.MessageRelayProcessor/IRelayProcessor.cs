// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor
{
    /// <summary>
    /// Connects a channel adapter to the Direct Line bot for the INBOUND direction: it ensures a durable
    /// conversation exists for the sender and posts the inbound activity to Direct Line. Outbound replies are
    /// delivered separately by the polling background service via an <see cref="IOutboundActivitySink"/> — there is
    /// no longer a per-call callback (which could not survive a restart).
    /// </summary>
    public interface IRelayProcessor
    {
        /// <summary>
        /// Ensure-or-create the durable conversation for the inbound activity's sender, then post the activity to
        /// Direct Line.
        /// </summary>
        /// <param name="activity">Inbound activity from the channel (its <c>From.Id</c> is the conversation key).</param>
        /// <param name="channelType">The channel the activity arrived on (stored on the row; drives sink resolution).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PostActivityAsync(Activity activity, string channelType, CancellationToken cancellationToken = default);
    }
}
