// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Durable home for the Instagram-user token. Implemented by <see cref="KeyVaultInstagramTokenStore"/>
    /// in production and <see cref="ConfigInstagramTokenStore"/> locally / in tests (no Azure).
    /// </summary>
    public interface IInstagramTokenStore
    {
        /// <summary>Returns the stored token state, or null when nothing is stored yet.</summary>
        Task<InstagramTokenState> GetAsync(CancellationToken cancellationToken);

        /// <summary>Persists the token state (creates a new version in durable stores).</summary>
        Task SetAsync(InstagramTokenState state, CancellationToken cancellationToken);
    }
}
