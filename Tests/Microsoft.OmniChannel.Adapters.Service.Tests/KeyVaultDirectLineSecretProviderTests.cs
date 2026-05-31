// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapters.Instagram;
using Microsoft.OmniChannel.Adapters.Service.Secrets;
using Microsoft.OmniChannel.MessageRelayProcessor;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.Adapters.Service.Tests
{
    /// <summary>
    /// P5 — the Key Vault-backed Direct Line secret provider (Service composition root). Reuses the P1
    /// <see cref="ISecretClientAdapter"/> seam; load-once + auto-seed + best-effort-persist, with a null expiry
    /// (the Direct Line secret does not auto-expire). xUnit + Moq, no Azure.
    /// </summary>
    public class KeyVaultDirectLineSecretProviderTests
    {
        private static IOptions<RelayProcessorConfiguration> Config(string seed = "dl-seed", string name = null) =>
            Options.Create(new RelayProcessorConfiguration { DirectLineSecret = seed, DirectLineSecretName = name });

        [Fact]
        public async Task Warm_UsesKvValue_WhenPresent()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync("DirectLineSecret", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new StoredSecret("kv-dl", null));
            var provider = new KeyVaultDirectLineSecretProvider(kv.Object, Config(), NullLogger<KeyVaultDirectLineSecretProvider>.Instance);

            await provider.WarmAsync(CancellationToken.None);

            Assert.Equal("kv-dl", provider.GetSecret());
            kv.Verify(c => c.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Warm_SeedsFromConfig_WhenKvEmpty_WithNullExpiry()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((StoredSecret)null);
            var provider = new KeyVaultDirectLineSecretProvider(kv.Object, Config(seed: "dl-seed"), NullLogger<KeyVaultDirectLineSecretProvider>.Instance);

            await provider.WarmAsync(CancellationToken.None);

            Assert.Equal("dl-seed", provider.GetSecret());
            // Expiry MUST be null — the Direct Line secret does not auto-expire.
            kv.Verify(c => c.SetSecretAsync("DirectLineSecret", "dl-seed", null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Warm_SeedSucceeds_EvenWhenKvWriteFails()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((StoredSecret)null);
            kv.Setup(c => c.SetSecretAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("Key Vault write unavailable"));
            var provider = new KeyVaultDirectLineSecretProvider(kv.Object, Config(seed: "dl-seed"), NullLogger<KeyVaultDirectLineSecretProvider>.Instance);

            await provider.WarmAsync(CancellationToken.None);

            // A transient persist failure must not block host start when we already hold a valid seed.
            Assert.Equal("dl-seed", provider.GetSecret());
        }

        [Fact]
        public void GetSecret_DefensivelyWarms_WhenNotWarmed()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync("DirectLineSecret", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new StoredSecret("kv-dl", null));
            var provider = new KeyVaultDirectLineSecretProvider(kv.Object, Config(), NullLogger<KeyVaultDirectLineSecretProvider>.Instance);

            // No explicit WarmAsync (e.g. a code path before Program.Main's warm) — GetSecret must self-warm once.
            Assert.Equal("kv-dl", provider.GetSecret());
        }

        [Fact]
        public async Task Warm_UsesConfiguredSecretName()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync("CustomName", It.IsAny<CancellationToken>())).ReturnsAsync(new StoredSecret("v", null));
            var provider = new KeyVaultDirectLineSecretProvider(kv.Object, Config(name: "CustomName"), NullLogger<KeyVaultDirectLineSecretProvider>.Instance);

            await provider.WarmAsync(CancellationToken.None);

            Assert.Equal("v", provider.GetSecret());
        }
    }
}
