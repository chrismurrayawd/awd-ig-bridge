// Copyright (c) Microsoft Corporation. All rights reserved.

using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Real <see cref="ISecretClientAdapter"/> over Azure Key Vault, authenticating with
    /// <see cref="DefaultAzureCredential"/> (the App Service's system-assigned managed identity in Azure;
    /// developer credentials locally). The identity needs Key Vault Secrets <b>Officer</b> (get + set), since
    /// the refresher writes new token versions.
    /// </summary>
    public class KeyVaultSecretClientAdapter : ISecretClientAdapter
    {
        private readonly SecretClient _client;

        public KeyVaultSecretClientAdapter(Uri vaultUri)
            : this(new SecretClient(vaultUri, new DefaultAzureCredential()))
        {
        }

        public KeyVaultSecretClientAdapter(SecretClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<StoredSecret> GetSecretAsync(string name, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _client.GetSecretAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
                var secret = response.Value;
                return new StoredSecret(secret.Value, secret.Properties?.ExpiresOn);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task SetSecretAsync(string name, string value, DateTimeOffset? expiresOn, CancellationToken cancellationToken)
        {
            var secret = new KeyVaultSecret(name, value);
            if (expiresOn.HasValue)
            {
                secret.Properties.ExpiresOn = expiresOn.Value;
            }

            await _client.SetSecretAsync(secret, cancellationToken).ConfigureAwait(false);
        }
    }
}
