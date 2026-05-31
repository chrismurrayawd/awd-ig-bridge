// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Supplies the Direct Line secret to <see cref="DirectLineGatewayFactory"/>. Defined in the relay base so the
    /// factory depends only on this seam (P5 swaps the factory's secret source with zero relay/poller change). The
    /// config-fallback (<see cref="ConfigDirectLineSecretProvider"/>) lives here; the Key Vault-backed implementation
    /// lives in the Service composition root, since only it can see both this interface and the adapter's
    /// <c>ISecretClientAdapter</c>.
    /// </summary>
    public interface IDirectLineSecretProvider
    {
        /// <summary>
        /// The Direct Line secret. SYNCHRONOUS by necessity: the gateway is built eagerly at host start and its
        /// constructor needs the secret string. Implementations cache the value (warmed via <see cref="WarmAsync"/>).
        /// </summary>
        string GetSecret();

        /// <summary>
        /// Pre-loads the secret into the in-memory cache. Called once from <c>Program.Main</c> before the host
        /// starts so <see cref="GetSecret"/> is a cache hit when the gateway is built. A no-op for the config-fallback.
        /// </summary>
        Task WarmAsync(CancellationToken cancellationToken);
    }
}
