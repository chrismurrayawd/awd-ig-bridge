// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// The single in-memory source of the current Meta app secret used to validate the inbound
    /// X-Hub-Signature-256 header. Loads from the durable <see cref="IAppSecretStore"/> on first use (seeding the
    /// store from the <c>AppSecret</c> app setting when empty) and caches it. Read-only: the app secret does not
    /// auto-expire and nothing in-process rotates it (rotation = update Key Vault + restart), so unlike
    /// <see cref="IInstagramTokenProvider"/> there is no SetAsync and no background refresher. Registered as a singleton.
    /// </summary>
    public interface IAppSecretProvider
    {
        /// <summary>The current app secret (initialising from the store / seeding from config on first use).</summary>
        Task<string> GetAppSecretAsync(CancellationToken cancellationToken = default);
    }
}
