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

                var seed = _configuration.Value?.DirectLineSecret;

                StoredSecret secret;
                try
                {
                    secret = await _secretClient.GetSecretAsync(SecretName, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // The Key Vault READ failed (transient blip / throttling / the managed identity not yet
                    // reachable — the failure class that bit P1). Don't take the whole bridge down at boot if we
                    // still hold a valid configured secret to serve (mirrors P1's lazy resilience): fall back to it
                    // for this process and re-read Key Vault on the next restart (the authoritative source). Fail
                    // LOUD only when there is genuinely no secret anywhere.
                    if (!string.IsNullOrWhiteSpace(seed))
                    {
                        Volatile.Write(ref _cached, seed);
                        _initialized = true;
                        _logger.LogError(ex,
                            "Failed to read the Direct Line secret from Key Vault secret {SecretName}; falling back to the configured DirectLineSecret for this process (Key Vault is re-read on the next restart).",
                            SecretName);
                        return;
                    }

                    _logger.LogCritical(ex,
                        "Failed to read the Direct Line secret from Key Vault secret {SecretName} and no DirectLineSecret app setting is configured to fall back to; the relay/poller cannot start.",
                        SecretName);
                    throw;
                }

                var value = secret?.Value;

                if (string.IsNullOrWhiteSpace(value))
                {
                    // Key Vault reachable but empty (first deploy): seed it from the DirectLineSecret app setting so
                    // Key Vault becomes the single source of truth from now on (written again only by a human rotation).
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

            // Not yet warmed. Program.Main warms this provider before app.Run(), and WarmAsync sets _initialized on
            // every non-throwing completion (Key Vault hit, first-deploy seed, or seed-fallback), so by the time the
            // host serves any request _initialized is already true — this defensive block is therefore reached only
            // when the Direct Line gateway is constructed during host start (or from a unit test), NOT from a served
            // request such as /api/TokenHealth. Block once: safe because the generic host has no SynchronizationContext
            // (no deadlock) and WarmAsync uses ConfigureAwait(false).
            WarmAsync(CancellationToken.None).GetAwaiter().GetResult();
            return Volatile.Read(ref _cached);
        }
    }
}
