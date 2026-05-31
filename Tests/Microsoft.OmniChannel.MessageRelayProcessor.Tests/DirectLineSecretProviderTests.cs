// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests
{
    /// <summary>
    /// P5 — the relay-side Direct Line secret seam: the config-fallback provider (no Azure, no warming) and the
    /// factory reading the secret from the provider (the single consumption point).
    /// </summary>
    public class DirectLineSecretProviderTests
    {
        private static IOptions<RelayProcessorConfiguration> Config(string secret) =>
            Options.Create(new RelayProcessorConfiguration { DirectLineSecret = secret });

        [Fact]
        public void ConfigProvider_GetSecret_ReturnsConfigValue_Sync()
        {
            var provider = new ConfigDirectLineSecretProvider(Config("dl-secret"));

            Assert.Equal("dl-secret", provider.GetSecret());
        }

        [Fact]
        public async Task ConfigProvider_WarmAsync_IsNoOp_GetStillReturnsConfig()
        {
            var provider = new ConfigDirectLineSecretProvider(Config("dl-secret"));

            await provider.WarmAsync(CancellationToken.None);

            Assert.Equal("dl-secret", provider.GetSecret());
        }

        [Fact]
        public void Factory_Create_BuildsGatewayFromProviderSecret()
        {
            var secretProvider = new Mock<IDirectLineSecretProvider>();
            secretProvider.Setup(p => p.GetSecret()).Returns("dl-secret");
            var factory = new DirectLineGatewayFactory(secretProvider.Object);

            using var gateway = factory.Create();

            Assert.NotNull(gateway);
            secretProvider.Verify(p => p.GetSecret(), Times.Once);
        }

        [Fact]
        public void Factory_Create_ThrowsWhenProviderSecretBlank()
        {
            var secretProvider = new Mock<IDirectLineSecretProvider>();
            secretProvider.Setup(p => p.GetSecret()).Returns(string.Empty);
            var factory = new DirectLineGatewayFactory(secretProvider.Object);

            // DirectLineGateway's ctor guards an empty secret, so a bad/missing secret fails loud, never silent.
            Assert.Throws<ArgumentException>(() => factory.Create());
        }
    }
}
