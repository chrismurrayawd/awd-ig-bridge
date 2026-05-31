// Copyright (c) Microsoft Corporation. All rights reserved.

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Creates <see cref="IDirectLineGateway"/> instances bound to the configured Direct Line secret. This is the
    /// single place the secret is consumed, so a future P5 move of the secret into Key Vault touches only the
    /// factory — not the relay or the poller.
    /// </summary>
    public interface IDirectLineGatewayFactory
    {
        IDirectLineGateway Create();
    }
}
