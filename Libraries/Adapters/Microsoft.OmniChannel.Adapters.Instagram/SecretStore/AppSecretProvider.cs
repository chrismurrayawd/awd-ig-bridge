// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Caches the current Meta app secret in memory, loading it from the durable store on first use and seeding the
    /// store from the <c>AppSecret</c> app setting when the store is empty (first deploy). Thread-safe singleton,
    /// shared by every inbound-webhook signature check. Mirrors <see cref="InstagramTokenProvider"/> minus the
    /// public SetAsync / expiry (the app secret is not refreshed in-process).
    /// </summary>
    public class AppSecretProvider : IAppSecretProvider
    {
        private readonly IAppSecretStore _store;
        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly ILogger<AppSecretProvider> _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private string _current;
        private volatile bool _initialized;

        public AppSecretProvider(
            IAppSecretStore store,
            IOptions<InstagramAdapterConfiguration> configuration,
            ILogger<AppSecretProvider> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> GetAppSecretAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            return Volatile.Read(ref _current);
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_initialized)
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_initialized)
                {
                    return;
                }

                var value = await _store.GetAsync(cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(value))
                {
                    // First deploy: the store is empty. Seed it from the AppSecret app setting so Key Vault becomes
                    // the single source of truth from now on (it is never written again except by a human rotation).
                    var seed = _configuration.Value?.AppSecret;
                    if (!string.IsNullOrWhiteSpace(seed))
                    {
                        value = seed;
                        try
                        {
                            await _store.SetAsync(seed, cancellationToken).ConfigureAwait(false);
                            _logger.LogInformation("Seeded the Meta app secret store from the AppSecret app setting.");
                        }
                        catch (Exception ex)
                        {
                            // Best-effort: a transient store / Key Vault write failure must not block inbound signature
                            // validation when we already hold a valid secret from config. The next restart re-seeds.
                            _logger.LogWarning(ex, "Failed to persist the seeded Meta app secret to the store; using it in-memory until the next restart.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "No Meta app secret available from the store or the AppSecret app setting; inbound signature validation will fail until one is provided.");
                        value = null;
                    }
                }

                Volatile.Write(ref _current, value);
                _initialized = true;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
