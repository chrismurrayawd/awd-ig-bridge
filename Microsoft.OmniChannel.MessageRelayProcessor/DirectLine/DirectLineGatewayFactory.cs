// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Creates raw <see cref="DirectLineGateway"/> instances bound to the Direct Line secret. The secret is read
    /// from an <see cref="IDirectLineSecretProvider"/> (config-backed or Key Vault-backed) — the single place the
    /// secret is consumed, so P5's move to Key Vault touches only the provider, not the relay/poller. In DI this
    /// factory is wrapped by the resilient decorator factory so callers transparently get retry/backoff.
    /// </summary>
    public sealed class DirectLineGatewayFactory : IDirectLineGatewayFactory
    {
        private readonly IDirectLineSecretProvider _secretProvider;

        public DirectLineGatewayFactory(IDirectLineSecretProvider secretProvider)
        {
            _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        }

        public IDirectLineGateway Create() => new DirectLineGateway(_secretProvider.GetSecret());
    }
}
