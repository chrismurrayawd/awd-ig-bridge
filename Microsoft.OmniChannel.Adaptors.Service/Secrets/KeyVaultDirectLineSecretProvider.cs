// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapters.Instagram;
using Microsoft.OmniChannel.MessageRelayProcessor;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Service.Secrets
{
    /// <summary>
    /// Key Vault-backed <see cref="IDirectLineSecretProvider"/>. This lives in the Service composition root because
    /// it is the only project that can see BOTH the relay's <see cref="IDirectLineSecretProvider"/> and the adapter's
    /// <see cref="ISecretClientAdapter"/> (the P1 Key Vault seam, reused as-is — no new Key Vault client). The secret
    /// does not auto-expire, so it is loaded once and cached: <see cref="WarmAsync"/> (called from Program.Main before
    /// the host starts) populates the cache; <see cref="GetSecret"/> then serves the eager gateway synchronously.
    /// Mirrors the P1 token provider's load-once + auto-seed + best-effort-persist semantics.
    /// </summary>
    public sealed class KeyVaultDirectLineSecretProvider : IDirectLineSecretProvider
    {
        private const string DefaultSecretName = "DirectLineSecret";

        private readonly ISecretClientAdapter _secretClient;
        private readonly IOptions<RelayProcessorConfiguration> _configuration;
        private readonly ILogger<KeyVaultDirectLineSecretProvider> _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private string _cached;
        private volatile bool _initialized;

        public KeyVaultDirectLineSecretProvider(
            ISecretClientAdapter secretClient,
            IOptions<RelayProcessorConfiguration> configuration,
            ILogger<KeyVaultDirectLineSecretProvider> logger)
        {
            _secretClient = secretClient ?? throw new ArgumentNullException(nameof(secretClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private string SecretName =>
            string.IsNullOrWhiteSpace(_configuration.Value?.DirectLineSecretName)
                ? DefaultSecretName
                : _configuration.Value.DirectLineSecretName;

        public async Task WarmAsync(CancellationToken cancellationToken)
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

                var secret = await _secretClient.GetSecretAsync(SecretName, cancellationToken).ConfigureAwait(false);
                var value = secret?.Value;

                if (string.IsNullOrWhiteSpace(value))
                {
                    // First deploy: Key Vault is empty. Seed it from the DirectLineSecret app setting so Key Vault
                    // becomes the single source of truth from now on (written again only by a human rotation).
                    var seed = _configuration.Value?.DirectLineSecret;
                    if (!string.IsNullOrWhiteSpace(seed))
                    {
                        value = seed;
                        try
                        {
                            await _secretClient.SetSecretAsync(SecretName, seed, expiresOn: null, cancellationToken).ConfigureAwait(false);
                            _logger.LogInformation("Seeded the Direct Line secret to Key Vault secret {SecretName}.", SecretName);
                        }
                        catch (Exception ex)
                        {
                            // Best-effort: a transient write failure must not block host start when we already hold
                            // a valid seed from config. The next restart re-seeds.
                            _logger.LogWarning(ex, "Failed to persist the seeded Direct Line secret to Key Vault; using it in-memory until the next restart.");
                        }
                    }
                    else
                    {
                        _logger.LogCritical(
                            "No Direct Line secret available from Key Vault secret {SecretName} or the DirectLineSecret app setting; the relay/poller cannot start a conversation until one is provided.",
                            SecretName);
                        value = null;
                    }
                }

                Volatile.Write(ref _cached, value);
                _initialized = true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public string GetSecret()
        {
            if (_initialized)
            {
                return Volatile.Read(ref _cached);
            }

            // Defensive: reached before Program.Main's warm step (e.g. a future code path or a unit test). Block ONCE
            // at startup, off any request thread (the generic host has no SynchronizationContext, so no deadlock),
            // to populate the cache. In production this is already a cache hit.
            WarmAsync(CancellationToken.None).GetAwaiter().GetResult();
            return Volatile.Read(ref _cached);
        }
    }
}
