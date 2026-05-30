// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Thin seam over the Key Vault secret operations the token store needs, so the store can be unit-tested
    /// without a live vault. Implemented for real by <see cref="KeyVaultSecretClientAdapter"/>.
    /// </summary>
    public interface ISecretClientAdapter
    {
        /// <summary>Reads a secret's value + expiry; returns null when the secret does not exist.</summary>
        Task<StoredSecret> GetSecretAsync(string name, CancellationToken cancellationToken);

        /// <summary>Writes a new version of the secret with the given value and (optional) expiry.</summary>
        Task SetSecretAsync(string name, string value, DateTimeOffset? expiresOn, CancellationToken cancellationToken);
    }

    /// <summary>A secret value paired with its expiry attribute (null when not set).</summary>
    public class StoredSecret
    {
        public StoredSecret(string value, DateTimeOffset? expiresOn)
        {
            Value = value;
            ExpiresOn = expiresOn;
        }

        public string Value { get; }

        public DateTimeOffset? ExpiresOn { get; }
    }
}
