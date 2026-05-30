// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Caches the current Instagram-user token in memory, loading it from the durable store on first use and
    /// seeding the store from the <c>PageAccessToken</c> app setting when the store is empty (first deploy).
    /// Thread-safe; shared (singleton) between the outbound sender and the refresher.
    /// </summary>
    public class InstagramTokenProvider : IInstagramTokenProvider
    {
        private readonly IInstagramTokenStore _store;
        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly ILogger<InstagramTokenProvider> _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private InstagramTokenState _current;
        private volatile bool _initialized;

        public InstagramTokenProvider(
            IInstagramTokenStore store,
            IOptions<InstagramAdapterConfiguration> configuration,
            ILogger<InstagramTokenProvider> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var state = Volatile.Read(ref _current);
            return state?.Token;
        }

        public async Task<InstagramTokenState> GetStateAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            return Volatile.Read(ref _current);
        }

        public async Task SetAsync(InstagramTokenState state, CancellationToken cancellationToken = default)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Token))
            {
                throw new ArgumentException("Token state must contain a non-empty token.", nameof(state));
            }

            await _store.SetAsync(state, cancellationToken).ConfigureAwait(false);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Volatile.Write(ref _current, state);
                _initialized = true;
            }
            finally
            {
                _gate.Release();
            }
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

                var state = await _store.GetAsync(cancellationToken).ConfigureAwait(false);

                if (state == null || string.IsNullOrWhiteSpace(state.Token))
                {
                    // First deploy: the store is empty. Seed it from the PageAccessToken app setting so Key
                    // Vault becomes the single source of truth from now on (the refresher updates it in place).
                    var seed = _configuration.Value?.PageAccessToken;
                    if (!string.IsNullOrWhiteSpace(seed))
                    {
                        state = new InstagramTokenState(seed, expiresOn: null);
                        try
                        {
                            await _store.SetAsync(state, cancellationToken).ConfigureAwait(false);
                            _logger.LogInformation("Seeded the Instagram token store from the PageAccessToken app setting.");
                        }
                        catch (Exception ex)
                        {
                            // Best-effort: a transient store / Key Vault write failure must not block outbound sends
                            // when we already hold a valid token from config. The refresher persists it next pass.
                            _logger.LogWarning(ex, "Failed to persist the seeded Instagram token to the store; using it in-memory until the next refresh.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "No Instagram access token available from the store or the PageAccessToken app setting; outbound sends will fail until one is provided.");
                        state = null;
                    }
                }

                Volatile.Write(ref _current, state);
                _initialized = true;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
