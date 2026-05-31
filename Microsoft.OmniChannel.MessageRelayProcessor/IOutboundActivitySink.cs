// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor
{
    /// <summary>
    /// Delivers agent-reply activities back out to a channel. Implemented by each channel adapter and resolved by
    /// channel via <see cref="OutboundSinkResolver"/>. This replaces the old un-persistable per-call callback
    /// closure: because the sink is resolved from DI by the conversation's stored channel — not captured from the
    /// original inbound request — a conversation rehydrated after a restart can deliver replies with no inbound
    /// trigger. Declared in the relay project (the adapter already references it), so no dependency cycle.
    /// </summary>
    public interface IOutboundActivitySink
    {
        /// <summary>
        /// Sends the given outbound activities to the customer. The recipient is taken from each activity's
        /// <c>ReplyToId</c> (set by the poller to the conversation's IGSID). Transient channel failures should be
        /// retried internally; a terminal failure should throw so the poller leaves the watermark un-advanced and
        /// logs loudly (the reply is retried next tick rather than silently lost).
        /// </summary>
        Task SendActivitiesAsync(IList<Activity> activities, CancellationToken cancellationToken);
    }
}
