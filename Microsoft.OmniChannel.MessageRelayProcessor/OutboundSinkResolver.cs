// Copyright (c) Microsoft Corporation. All rights reserved.

namespace Microsoft.OmniChannel.MessageRelayProcessor
{
    /// <summary>
    /// Resolves the <see cref="IOutboundActivitySink"/> for a channel, by the channel-type string stored on the
    /// conversation row. Mirrors the existing <c>AdapterServiceResolver</c> delegate; registered in DI as a switch
    /// over the known channels and should throw for an unknown channel.
    /// </summary>
    /// <param name="channelType">The conversation's channel (e.g. "Instagram").</param>
    /// <returns>The sink that delivers replies for that channel.</returns>
    public delegate IOutboundActivitySink OutboundSinkResolver(string channelType);
}
