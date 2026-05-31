// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.Adapters.Instagram.Tests
{
    /// <summary>
    /// P5 secret hygiene — the Meta app-secret store/provider stack. Mirrors the P1 token tests
    /// (<see cref="InstagramTokenTests"/>): xUnit + Moq, no Azure. The app secret has no expiry, so the
    /// Key Vault writes must pass a null ExpiresOn and there is no background refresher.
    /// </summary>
    public class AppSecretTests
    {
        private static IOptions<InstagramAdapterConfiguration> Config(Action<InstagramAdapterConfiguration> tweak = null)
        {
            var config = new InstagramAdapterConfiguration
            {
                AppSecret = "cfg-app-secret",
                IgBusinessId = "17841440469975661",
            };
            tweak?.Invoke(config);
            return Options.Create(config);
        }

        // ---- Key Vault store (via the reused ISecretClientAdapter seam) -----

        [Fact]
        public async Task KvStore_Get_ReturnsValue()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync("MetaAppSecret", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new StoredSecret("vault-app-secret", null));
            var store = new KeyVaultAppSecretStore(kv.Object, Config(), NullLogger<KeyVaultAppSecretStore>.Instance);

            Assert.Equal("vault-app-secret", await store.GetAsync(CancellationToken.None));
        }

        [Fact]
        public async Task KvStore_Get_ReturnsNull_WhenSecretMissing()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((StoredSecret)null);
            var store = new KeyVaultAppSecretStore(kv.Object, Config(), NullLogger<KeyVaultAppSecretStore>.Instance);

            Assert.Null(await store.GetAsync(CancellationToken.None));
        }

        [Fact]
        public async Task KvStore_Get_UsesConfiguredSecretName()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync("CustomName", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new StoredSecret("v", null));
            var store = new KeyVaultAppSecretStore(kv.Object, Config(c => c.AppSecretName = "CustomName"), NullLogger<KeyVaultAppSecretStore>.Instance);

            Assert.Equal("v", await store.GetAsync(CancellationToken.None));
        }

        [Fact]
        public async Task KvStore_Set_WritesValueWithNullExpiry()
        {
            var kv = new Mock<ISecretClientAdapter>();
            var store = new KeyVaultAppSecretStore(kv.Object, Config(), NullLogger<KeyVaultAppSecretStore>.Instance);

            await store.SetAsync("new-secret", CancellationToken.None);

            // Expiry MUST be null — the app secret does not auto-expire (no slide-forward refresher).
            kv.Verify(c => c.SetSecretAsync("MetaAppSecret", "new-secret", null, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---- Config store (no Azure) ----------------------------------------

        [Fact]
        public async Task ConfigStore_ReturnsConfigSecret_ThenInMemoryAfterSet()
        {
            var store = new ConfigAppSecretStore(Config(c => c.AppSecret = "cfg"), NullLogger<ConfigAppSecretStore>.Instance);

            Assert.Equal("cfg", await store.GetAsync(CancellationToken.None));

            await store.SetAsync("seeded", CancellationToken.None);
            Assert.Equal("seeded", await store.GetAsync(CancellationToken.None));
        }

        // ---- Provider -------------------------------------------------------

        [Fact]
        public async Task Provider_SeedsStoreFromConfig_WhenStoreEmpty()
        {
            var store = new Mock<IAppSecretStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string)null);
            var provider = new AppSecretProvider(store.Object, Config(c => c.AppSecret = "seed"), NullLogger<AppSecretProvider>.Instance);

            Assert.Equal("seed", await provider.GetAppSecretAsync());
            store.Verify(s => s.SetAsync("seed", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Provider_UsesStoreSecret_WhenPresent()
        {
            var store = new Mock<IAppSecretStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync("vault");
            var provider = new AppSecretProvider(store.Object, Config(c => c.AppSecret = "seed"), NullLogger<AppSecretProvider>.Instance);

            Assert.Equal("vault", await provider.GetAppSecretAsync());
            store.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Provider_CachesAcrossCalls_LoadsOnce()
        {
            var store = new Mock<IAppSecretStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync("vault");
            var provider = new AppSecretProvider(store.Object, Config(), NullLogger<AppSecretProvider>.Instance);

            await provider.GetAppSecretAsync();
            await provider.GetAppSecretAsync();

            store.Verify(s => s.GetAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Provider_SeedSucceeds_EvenWhenStoreWriteFails()
        {
            var store = new Mock<IAppSecretStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string)null);
            store.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Key Vault write unavailable"));
            var provider = new AppSecretProvider(store.Object, Config(c => c.AppSecret = "seed"), NullLogger<AppSecretProvider>.Instance);

            // A transient persist failure must not block use of the valid in-memory seed.
            Assert.Equal("seed", await provider.GetAppSecretAsync());
        }

        [Fact]
        public async Task Provider_DoesNotCacheFailedInit()
        {
            var store = new Mock<IAppSecretStore>();
            store.SetupSequence(s => s.GetAsync(It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Key Vault unreachable"))
                 .ReturnsAsync("recovered");
            var provider = new AppSecretProvider(store.Object, Config(), NullLogger<AppSecretProvider>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAppSecretAsync());
            Assert.Equal("recovered", await provider.GetAppSecretAsync());
            store.Verify(s => s.GetAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Provider_NoSecretAnywhere_ReturnsNull()
        {
            var store = new Mock<IAppSecretStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string)null);
            var provider = new AppSecretProvider(store.Object, Config(c => c.AppSecret = null), NullLogger<AppSecretProvider>.Instance);

            Assert.Null(await provider.GetAppSecretAsync());
            store.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Provider_SeedPersistFailure_DoesNotLeakSecretInLog()
        {
            // The best-effort persist logs a warning on failure — it must log the EXCEPTION, never the secret value.
            var store = new Mock<IAppSecretStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string)null);
            store.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("write failed"));
            var logger = new RecordingLogger<AppSecretProvider>();
            var provider = new AppSecretProvider(store.Object, Config(c => c.AppSecret = "SUPER_SECRET_VALUE"), logger);

            await provider.GetAppSecretAsync();

            Assert.DoesNotContain(logger.Entries, e => e.Message.Contains("SUPER_SECRET_VALUE"));
        }

        // ---- ValidateSignatureAsync reads the secret from the provider --------

        [Fact]
        public async Task ValidateSignatureAsync_ValidSignature_ReturnsTrue()
        {
            var body = Encoding.UTF8.GetBytes("{\"object\":\"instagram\"}");
            var provider = new Mock<IAppSecretProvider>();
            provider.Setup(p => p.GetAppSecretAsync(It.IsAny<CancellationToken>())).ReturnsAsync("the-secret");
            var wrapper = WrapperWith(provider.Object);

            Assert.True(await wrapper.ValidateSignatureAsync(body, RequestWithSignature(Sign(body, "the-secret"))));
        }

        [Fact]
        public async Task ValidateSignatureAsync_WrongSecret_ReturnsFalse()
        {
            var body = Encoding.UTF8.GetBytes("{\"object\":\"instagram\"}");
            var provider = new Mock<IAppSecretProvider>();
            provider.Setup(p => p.GetAppSecretAsync(It.IsAny<CancellationToken>())).ReturnsAsync("the-secret");
            var wrapper = WrapperWith(provider.Object);

            // The body is signed with a DIFFERENT secret than the provider yields → mismatch.
            Assert.False(await wrapper.ValidateSignatureAsync(body, RequestWithSignature(Sign(body, "other-secret"))));
        }

        [Fact]
        public async Task ValidateSignatureAsync_BlankSecret_ReturnsFalse_FailClosed()
        {
            var body = Encoding.UTF8.GetBytes("{}");
            var provider = new Mock<IAppSecretProvider>();
            provider.Setup(p => p.GetAppSecretAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string)null);
            var wrapper = WrapperWith(provider.Object);

            // No secret available → fail closed (→ 403), must NOT throw.
            Assert.False(await wrapper.ValidateSignatureAsync(body, RequestWithSignature("sha256=deadbeef")));
        }

        [Fact]
        public void Wrapper_DoesNotThrow_WhenConfigAppSecretEmpty()
        {
            // The ctor AppSecret non-empty throw was dropped (the provider owns the secret now); IgBusinessId still required.
            var tokenProvider = new Mock<IInstagramTokenProvider>();
            var appSecretProvider = new Mock<IAppSecretProvider>();

            var ex = Record.Exception(() =>
                new InstagramClientWrapper(Config(c => c.AppSecret = null), tokenProvider.Object, appSecretProvider.Object, new HttpClient()));

            Assert.Null(ex);
        }

        private static InstagramClientWrapper WrapperWith(IAppSecretProvider appSecretProvider)
        {
            var tokenProvider = new Mock<IInstagramTokenProvider>();
            return new InstagramClientWrapper(Config(), tokenProvider.Object, appSecretProvider, new HttpClient());
        }

        private static HttpRequest RequestWithSignature(string signature)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers[InstagramClientWrapper.SignatureHeaderName] = signature;
            return context.Request;
        }

        private static string Sign(byte[] body, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(body);
            var builder = new StringBuilder("sha256=");
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new List<(LogLevel, string)>();

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
