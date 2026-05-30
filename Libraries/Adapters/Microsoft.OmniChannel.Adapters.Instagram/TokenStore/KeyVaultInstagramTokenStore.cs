// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Durable token store backed by Azure Key Vault. The token expiry is tracked via the KV secret's
    /// <c>ExpiresOn</c> attribute (set on every write), so it is the single source of truth for remaining
    /// lifetime — no separate metadata store.
    /// </summary>
    public class KeyVaultInstagramTokenStore : IInstagramTokenStore
    {
        private const string DefaultSecretName = "IgUserAccessToken";

        private readonly ISecretClientAdapter _secretClient;
        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly ILogger<KeyVaultInstagramTokenStore> _logger;

        public KeyVaultInstagramTokenStore(
            ISecretClientAdapter secretClient,
            IOptions<InstagramAdapterConfiguration> configuration,
            ILogger<KeyVaultInstagramTokenStore> logger)
        {
            _secretClient = secretClient ?? throw new ArgumentNullException(nameof(secretClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private string SecretName =>
            string.IsNullOrWhiteSpace(_configuration.Value?.TokenSecretName)
                ? DefaultSecretName
                : _configuration.Value.TokenSecretName;

        public async Task<InstagramTokenState> GetAsync(CancellationToken cancellationToken)
        {
            var secret = await _secretClient.GetSecretAsync(SecretName, cancellationToken).ConfigureAwait(false);
            if (secret == null || string.IsNullOrWhiteSpace(secret.Value))
            {
                return null;
            }

            return new InstagramTokenState(secret.Value, secret.ExpiresOn);
        }

        public async Task SetAsync(InstagramTokenState state, CancellationToken cancellationToken)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Token))
            {
                throw new ArgumentException("Token state must contain a non-empty token.", nameof(state));
            }

            await _secretClient.SetSecretAsync(SecretName, state.Token, state.ExpiresOn, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Persisted the Instagram token to Key Vault secret {SecretName} (expires {Expiry}).",
                SecretName,
                state.ExpiresOn);
        }
    }
}
