// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using System;
using System.Collections.Generic;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles
{
    /// <summary>Hands out a supplied <see cref="IDirectLineGateway"/> (or one per call) and records what it created.</summary>
    public sealed class FakeDirectLineGatewayFactory : IDirectLineGatewayFactory
    {
        private readonly Func<IDirectLineGateway> _create;

        public FakeDirectLineGatewayFactory(IDirectLineGateway single)
            : this(() => single)
        {
        }

        public FakeDirectLineGatewayFactory(Func<IDirectLineGateway> create)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
        }

        public List<IDirectLineGateway> Created { get; } = new List<IDirectLineGateway>();

        public IDirectLineGateway Create()
        {
            var gateway = _create();
            Created.Add(gateway);
            return gateway;
        }
    }
}
