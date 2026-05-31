// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Durable home for the Meta app secret (P5 secret hygiene). Implemented by
    /// <see cref="KeyVaultAppSecretStore"/> in production and <see cref="ConfigAppSecretStore"/> locally / in
    /// tests (no Azure). Mirrors <see cref="IInstagramTokenStore"/>, but the value is a plain string with no
    /// expiry — the app secret does not auto-expire (so there is no background refresher; cf. P1's token).
    /// </summary>
    public interface IAppSecretStore
    {
        /// <summary>Returns the stored app secret, or null when nothing is stored yet.</summary>
        Task<string> GetAsync(CancellationToken cancellationToken);

        /// <summary>Persists the app secret (creates a new version in durable stores). Used only by the auto-seed.</summary>
        Task SetAsync(string value, CancellationToken cancellationToken);
    }
}
