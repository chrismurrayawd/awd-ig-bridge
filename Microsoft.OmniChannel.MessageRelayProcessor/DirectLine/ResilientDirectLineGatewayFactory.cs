// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.OmniChannel.MessageRelayProcessor.Resilience;
using System;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Decorates an inner <see cref="IDirectLineGatewayFactory"/> so every gateway it creates is wrapped in a
    /// <see cref="ResilientDirectLineGateway"/>. Registered in DI as the <see cref="IDirectLineGatewayFactory"/>
    /// callers depend on, so retry/backoff is transparent.
    /// </summary>
    public sealed class ResilientDirectLineGatewayFactory : IDirectLineGatewayFactory
    {
        private readonly IDirectLineGatewayFactory _inner;
        private readonly RetryOptions _options;
        private readonly ILogger<ResilientDirectLineGateway> _logger;

        public ResilientDirectLineGatewayFactory(IDirectLineGatewayFactory inner, ILogger<ResilientDirectLineGateway> logger)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = new RetryOptions();
        }

        public IDirectLineGateway Create()
        {
            var raw = _inner.Create();
            var retry = new RetryExecutor(_options, _logger);
            return new ResilientDirectLineGateway(raw, retry);
        }
    }
}
