// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Options;
using System;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Creates raw <see cref="DirectLineGateway"/> instances bound to the configured Direct Line secret. In DI it
    /// is wrapped by the resilient decorator factory so callers transparently get retry/backoff.
    /// </summary>
    public sealed class DirectLineGatewayFactory : IDirectLineGatewayFactory
    {
        private readonly IOptions<RelayProcessorConfiguration> _configuration;

        public DirectLineGatewayFactory(IOptions<RelayProcessorConfiguration> configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IDirectLineGateway Create() => new DirectLineGateway(_configuration.Value?.DirectLineSecret);
    }
}
