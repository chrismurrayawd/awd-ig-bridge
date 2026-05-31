// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Fallback app-secret store used when Key Vault is not configured (KeyVault:Uri empty) — for local dev and
    /// tests. Reads the secret from the <c>AppSecret</c> app setting; a seeded value is kept in memory only and
    /// does NOT survive a restart (logged as a warning), so this is not for production. Mirrors
    /// <see cref="ConfigInstagramTokenStore"/>.
    /// </summary>
    public class ConfigAppSecretStore : IAppSecretStore
    {
        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly ILogger<ConfigAppSecretStore> _logger;
        private readonly object _lock = new object();

        private string _inMemory;

        public ConfigAppSecretStore(
            IOptions<InstagramAdapterConfiguration> configuration,
            ILogger<ConfigAppSecretStore> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<string> GetAsync(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_inMemory != null)
                {
                    return Task.FromResult(_inMemory);
                }
            }

            var value = _configuration.Value?.AppSecret;
            return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
        }

        public Task SetAsync(string value, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _inMemory = value;
            }

            _logger.LogWarning(
                "Key Vault is not configured (KeyVault:Uri is empty) — the Meta app secret is held in memory only " +
                "and will NOT survive an app restart. Configure Key Vault for durable secret persistence.");
            return Task.CompletedTask;
        }
    }
}
