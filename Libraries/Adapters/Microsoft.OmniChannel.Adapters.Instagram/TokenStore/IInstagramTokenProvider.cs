// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// The single in-memory source of the current Instagram-user token. The outbound sender reads from this
    /// at send time and the background refresher writes to it, so a refresh takes effect without a restart.
    /// Registered as a singleton; backed by an <see cref="IInstagramTokenStore"/> for durability.
    /// </summary>
    public interface IInstagramTokenProvider
    {
        /// <summary>The current token value (initialising from the store / seeding from config on first use).</summary>
        Task<string> GetTokenAsync(CancellationToken cancellationToken = default);

        /// <summary>The current token state including its known expiry.</summary>
        Task<InstagramTokenState> GetStateAsync(CancellationToken cancellationToken = default);

        /// <summary>Replaces the current token (in memory) and persists it to the store.</summary>
        Task SetAsync(InstagramTokenState state, CancellationToken cancellationToken = default);
    }
}
