// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Fallback Direct Line secret provider used when Key Vault is not configured (KeyVault:Uri empty) — for local
    /// dev and tests. Reads the secret straight from <see cref="RelayProcessorConfiguration.DirectLineSecret"/>
    /// synchronously and needs no warming, so the no-Azure DI graph resolves <c>IDirectLineGateway</c> with no
    /// Key Vault access.
    /// </summary>
    public sealed class ConfigDirectLineSecretProvider : IDirectLineSecretProvider
    {
        private readonly IOptions<RelayProcessorConfiguration> _configuration;

        public ConfigDirectLineSecretProvider(IOptions<RelayProcessorConfiguration> configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public string GetSecret() => _configuration.Value?.DirectLineSecret;

        public Task WarmAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
