// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Durable Meta-app-secret store backed by Azure Key Vault, reusing the same <see cref="ISecretClientAdapter"/>
    /// seam (and DI singleton) as the P1 token store — no second Key Vault client. The secret is written with no
    /// <c>ExpiresOn</c> (it does not auto-expire), so unlike P1 there is no slide-forward refresher: the only writes
    /// are the one-time auto-seed and a human rotation.
    /// </summary>
    public class KeyVaultAppSecretStore : IAppSecretStore
    {
        private const string DefaultSecretName = "MetaAppSecret";

        private readonly ISecretClientAdapter _secretClient;
        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly ILogger<KeyVaultAppSecretStore> _logger;

        public KeyVaultAppSecretStore(
            ISecretClientAdapter secretClient,
            IOptions<InstagramAdapterConfiguration> configuration,
            ILogger<KeyVaultAppSecretStore> logger)
        {
            _secretClient = secretClient ?? throw new ArgumentNullException(nameof(secretClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private string SecretName =>
            string.IsNullOrWhiteSpace(_configuration.Value?.AppSecretName)
                ? DefaultSecretName
                : _configuration.Value.AppSecretName;

        public async Task<string> GetAsync(CancellationToken cancellationToken)
        {
            var secret = await _secretClient.GetSecretAsync(SecretName, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(secret?.Value) ? null : secret.Value;
        }

        public async Task SetAsync(string value, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("App secret must be a non-empty value.", nameof(value));
            }

            await _secretClient.SetSecretAsync(SecretName, value, expiresOn: null, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Persisted the Meta app secret to Key Vault secret {SecretName}.", SecretName);
        }
    }
}
