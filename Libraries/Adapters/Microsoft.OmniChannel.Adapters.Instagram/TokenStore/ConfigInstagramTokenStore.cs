// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Fallback token store used when Key Vault is not configured (KeyVault:Uri empty) — for local dev and
    /// tests. Reads the token from the <c>PageAccessToken</c> app setting; a refreshed token is kept in memory
    /// only and does NOT survive a restart (logged as a warning), so this is not for production.
    /// </summary>
    public class ConfigInstagramTokenStore : IInstagramTokenStore
    {
        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly ILogger<ConfigInstagramTokenStore> _logger;
        private readonly object _lock = new object();

        private InstagramTokenState _inMemory;

        public ConfigInstagramTokenStore(
            IOptions<InstagramAdapterConfiguration> configuration,
            ILogger<ConfigInstagramTokenStore> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<InstagramTokenState> GetAsync(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_inMemory != null)
                {
                    return Task.FromResult(_inMemory);
                }
            }

            var token = _configuration.Value?.PageAccessToken;
            return Task.FromResult(string.IsNullOrWhiteSpace(token)
                ? null
                : new InstagramTokenState(token, expiresOn: null));
        }

        public Task SetAsync(InstagramTokenState state, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _inMemory = state;
            }

            _logger.LogWarning(
                "Key Vault is not configured (KeyVault:Uri is empty) — the refreshed Instagram token is held in " +
                "memory only and will NOT survive an app restart. Configure Key Vault for durable token persistence.");
            return Task.CompletedTask;
        }
    }
}
