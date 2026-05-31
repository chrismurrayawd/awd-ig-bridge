// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Thin seam over the Direct Line SDK (a client bound to one Direct Line secret) so the relay and poller are
    /// unit-testable without a live Direct Line — mirroring P1's <c>ISecretClientAdapter</c>. Named "gateway" to
    /// avoid colliding with the SDK's own <c>IDirectLineClient</c>. Bound to the configured secret by the factory;
    /// callers never see the secret or the SDK types.
    /// </summary>
    public interface IDirectLineGateway : IDisposable
    {
        /// <summary>Starts a new Direct Line conversation and returns its durable ConversationId.</summary>
        Task<string> StartConversationAsync(CancellationToken cancellationToken);

        /// <summary>Posts an inbound activity to the conversation.</summary>
        Task PostActivityAsync(string conversationId, Activity activity, CancellationToken cancellationToken);

        /// <summary>Retrieves the activities after the given watermark (null/empty watermark = from the start).</summary>
        Task<DirectLineActivitySet> GetActivitiesAsync(string conversationId, string watermark, CancellationToken cancellationToken);
    }
}
