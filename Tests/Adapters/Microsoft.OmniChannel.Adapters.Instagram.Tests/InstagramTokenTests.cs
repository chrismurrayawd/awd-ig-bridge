// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.Adapters.Instagram.Tests
{
    public class InstagramTokenTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private static IOptions<InstagramAdapterConfiguration> Config(Action<InstagramAdapterConfiguration> tweak = null)
        {
            var config = new InstagramAdapterConfiguration
            {
                AppSecret = "app-secret",
                IgBusinessId = "17841440469975661",
                PageAccessToken = "seed-token",
                GraphApiVersion = "v21.0",
                TokenSecretName = "IgUserAccessToken",
                TokenRefreshThresholdDays = 20,
                TokenRefreshCheckIntervalHours = 12,
            };
            tweak?.Invoke(config);
            return Options.Create(config);
        }

        // ---- Refresh client -------------------------------------------------

        [Fact]
        public async Task RefreshAsync_Success_ParsesTokenAndExpiry()
        {
            string capturedUrl = null;
            var handler = new StubHttpMessageHandler(request =>
            {
                capturedUrl = request.RequestUri.ToString();
                return Json(HttpStatusCode.OK, "{\"access_token\":\"IGAA_new\",\"token_type\":\"bearer\",\"expires_in\":5184000}");
            });
            var client = new InstagramTokenRefreshClient(new HttpClient(handler), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshClient>.Instance);

            var result = await client.RefreshAsync("old-token", CancellationToken.None);

            Assert.Equal("IGAA_new", result.Token);
            Assert.Equal(Now.AddSeconds(5184000), result.ExpiresOn);
            Assert.Contains("/refresh_access_token", capturedUrl);
            Assert.Contains("grant_type=ig_refresh_token", capturedUrl);
            Assert.Contains("access_token=old-token", capturedUrl);
        }

        [Fact]
        public async Task RefreshAsync_Code190_ThrowsExpired()
        {
            var handler = new StubHttpMessageHandler(_ =>
                Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Error validating access token: Session has expired\",\"code\":190}}"));
            var client = new InstagramTokenRefreshClient(new HttpClient(handler), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshClient>.Instance);

            await Assert.ThrowsAsync<InstagramTokenExpiredException>(() => client.RefreshAsync("x", CancellationToken.None));
        }

        [Fact]
        public async Task RefreshAsync_TooFresh_ThrowsTooFresh()
        {
            var handler = new StubHttpMessageHandler(_ =>
                Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"The token must be at least 24 hours old to be refreshed.\",\"code\":1}}"));
            var client = new InstagramTokenRefreshClient(new HttpClient(handler), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshClient>.Instance);

            await Assert.ThrowsAsync<InstagramTokenTooFreshException>(() => client.RefreshAsync("x", CancellationToken.None));
        }

        [Fact]
        public async Task RefreshAsync_OtherError_ThrowsInvalidOperation()
        {
            var handler = new StubHttpMessageHandler(_ =>
                Json(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"temporary\",\"code\":2}}"));
            var client = new InstagramTokenRefreshClient(new HttpClient(handler), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshClient>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshAsync("x", CancellationToken.None));
        }

        [Fact]
        public async Task RefreshAsync_UnparseableSuccessBody_DoesNotLeakToken()
        {
            // A 2xx body that fails to parse to a usable token. On the real success path the body contains the
            // freshly minted token, so the thrown exception must NOT echo the body.
            var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.OK, "{\"access_token\":\"IGAA_SECRET_TOKEN\""));
            var client = new InstagramTokenRefreshClient(new HttpClient(handler), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshClient>.Instance);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshAsync("x", CancellationToken.None));

            Assert.DoesNotContain("IGAA_SECRET_TOKEN", ex.Message);
        }

        // ---- Policy ---------------------------------------------------------

        [Fact]
        public void ShouldRefresh_FarFromExpiry_False()
        {
            var state = new InstagramTokenState("t", Now.AddDays(50));
            Assert.False(InstagramTokenRefreshPolicy.ShouldRefresh(state, Now, thresholdDays: 20));
        }

        [Fact]
        public void ShouldRefresh_WithinThreshold_True()
        {
            var state = new InstagramTokenState("t", Now.AddDays(10));
            Assert.True(InstagramTokenRefreshPolicy.ShouldRefresh(state, Now, thresholdDays: 20));
        }

        [Fact]
        public void ShouldRefresh_UnknownExpiry_True()
        {
            var state = new InstagramTokenState("t", expiresOn: null);
            Assert.True(InstagramTokenRefreshPolicy.ShouldRefresh(state, Now, thresholdDays: 20));
        }

        [Fact]
        public void ShouldRefresh_NoToken_False()
        {
            Assert.False(InstagramTokenRefreshPolicy.ShouldRefresh(new InstagramTokenState(null, null), Now, 20));
            Assert.False(InstagramTokenRefreshPolicy.ShouldRefresh(null, Now, 20));
        }

        // ---- Config store ---------------------------------------------------

        [Fact]
        public async Task ConfigStore_ReturnsConfigToken_ThenInMemoryAfterSet()
        {
            var store = new ConfigInstagramTokenStore(Config(c => c.PageAccessToken = "cfg-token"), NullLogger<ConfigInstagramTokenStore>.Instance);

            Assert.Equal("cfg-token", (await store.GetAsync(CancellationToken.None)).Token);

            await store.SetAsync(new InstagramTokenState("refreshed", Now.AddDays(60)), CancellationToken.None);
            var after = await store.GetAsync(CancellationToken.None);
            Assert.Equal("refreshed", after.Token);
            Assert.Equal(Now.AddDays(60), after.ExpiresOn);
        }

        // ---- Key Vault store (via fake seam) --------------------------------

        [Fact]
        public async Task KvStore_Get_ReturnsStateWithExpiry()
        {
            var expiry = Now.AddDays(40);
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync("IgUserAccessToken", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new StoredSecret("vault-token", expiry));
            var store = new KeyVaultInstagramTokenStore(kv.Object, Config(), NullLogger<KeyVaultInstagramTokenStore>.Instance);

            var state = await store.GetAsync(CancellationToken.None);

            Assert.Equal("vault-token", state.Token);
            Assert.Equal(expiry, state.ExpiresOn);
        }

        [Fact]
        public async Task KvStore_Get_ReturnsNull_WhenSecretMissing()
        {
            var kv = new Mock<ISecretClientAdapter>();
            kv.Setup(c => c.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((StoredSecret)null);
            var store = new KeyVaultInstagramTokenStore(kv.Object, Config(), NullLogger<KeyVaultInstagramTokenStore>.Instance);

            Assert.Null(await store.GetAsync(CancellationToken.None));
        }

        [Fact]
        public async Task KvStore_Set_WritesValueAndExpiry()
        {
            var expiry = Now.AddDays(60);
            var kv = new Mock<ISecretClientAdapter>();
            var store = new KeyVaultInstagramTokenStore(kv.Object, Config(), NullLogger<KeyVaultInstagramTokenStore>.Instance);

            await store.SetAsync(new InstagramTokenState("new-token", expiry), CancellationToken.None);

            kv.Verify(c => c.SetSecretAsync("IgUserAccessToken", "new-token", expiry, It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---- Provider -------------------------------------------------------

        [Fact]
        public async Task Provider_SeedsStoreFromConfig_WhenStoreEmpty()
        {
            var store = new Mock<IInstagramTokenStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((InstagramTokenState)null);
            var provider = new InstagramTokenProvider(store.Object, Config(c => c.PageAccessToken = "seed"), NullLogger<InstagramTokenProvider>.Instance);

            var token = await provider.GetTokenAsync();

            Assert.Equal("seed", token);
            store.Verify(s => s.SetAsync(It.Is<InstagramTokenState>(st => st.Token == "seed"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Provider_UsesStoreToken_WhenPresent()
        {
            var store = new Mock<IInstagramTokenStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramTokenState("stored", Now.AddDays(30)));
            var provider = new InstagramTokenProvider(store.Object, Config(c => c.PageAccessToken = "seed"), NullLogger<InstagramTokenProvider>.Instance);

            Assert.Equal("stored", await provider.GetTokenAsync());
            store.Verify(s => s.SetAsync(It.IsAny<InstagramTokenState>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Provider_SetAsync_UpdatesCurrentAndStore()
        {
            var store = new Mock<IInstagramTokenStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramTokenState("a", null));
            var provider = new InstagramTokenProvider(store.Object, Config(), NullLogger<InstagramTokenProvider>.Instance);

            Assert.Equal("a", await provider.GetTokenAsync());

            await provider.SetAsync(new InstagramTokenState("b", Now.AddDays(60)));

            Assert.Equal("b", await provider.GetTokenAsync());
            store.Verify(s => s.SetAsync(It.Is<InstagramTokenState>(st => st.Token == "b"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Provider_SeedSucceeds_EvenWhenStoreWriteFails()
        {
            var store = new Mock<IInstagramTokenStore>();
            store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((InstagramTokenState)null);
            store.Setup(s => s.SetAsync(It.IsAny<InstagramTokenState>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Key Vault write unavailable"));
            var provider = new InstagramTokenProvider(store.Object, Config(c => c.PageAccessToken = "seed"), NullLogger<InstagramTokenProvider>.Instance);

            // A transient store / Key Vault write failure must not block use of the valid in-memory seed token.
            Assert.Equal("seed", await provider.GetTokenAsync());
        }

        [Fact]
        public async Task Provider_DoesNotCacheFailedInit()
        {
            var store = new Mock<IInstagramTokenStore>();
            store.SetupSequence(s => s.GetAsync(It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Key Vault unreachable"))
                 .ReturnsAsync(new InstagramTokenState("recovered", Now.AddDays(30)));
            var provider = new InstagramTokenProvider(store.Object, Config(), NullLogger<InstagramTokenProvider>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync());
            Assert.Equal("recovered", await provider.GetTokenAsync());
            store.Verify(s => s.GetAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        // ---- Refresh service (orchestration) --------------------------------

        [Fact]
        public async Task RefreshService_RefreshesAndPersists_WhenDue()
        {
            var provider = new Mock<IInstagramTokenProvider>();
            provider.Setup(p => p.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramTokenState("old", expiresOn: null));
            var newExpiry = Now.AddDays(60);
            var client = new Mock<IInstagramTokenRefreshClient>();
            client.Setup(c => c.RefreshAsync("old", It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramTokenRefreshResult("new", newExpiry));
            var service = new InstagramTokenRefreshService(provider.Object, client.Object, Config(), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshService>.Instance);

            await service.RunOnceAsync(CancellationToken.None);

            provider.Verify(p => p.SetAsync(It.Is<InstagramTokenState>(s => s.Token == "new" && s.ExpiresOn == newExpiry), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RefreshService_SkipsRefresh_WhenHealthy()
        {
            var provider = new Mock<IInstagramTokenProvider>();
            provider.Setup(p => p.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramTokenState("tok", Now.AddDays(50)));
            var client = new Mock<IInstagramTokenRefreshClient>();
            var service = new InstagramTokenRefreshService(provider.Object, client.Object, Config(), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshService>.Instance);

            await service.RunOnceAsync(CancellationToken.None);

            client.Verify(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            provider.Verify(p => p.SetAsync(It.IsAny<InstagramTokenState>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RefreshService_TooFresh_DoesNotPersist_NoThrow()
        {
            var provider = new Mock<IInstagramTokenProvider>();
            provider.Setup(p => p.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramTokenState("tok", expiresOn: null));
            var client = new Mock<IInstagramTokenRefreshClient>();
            client.Setup(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InstagramTokenTooFreshException("too fresh"));
            var service = new InstagramTokenRefreshService(provider.Object, client.Object, Config(), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshService>.Instance);

            await service.RunOnceAsync(CancellationToken.None);

            provider.Verify(p => p.SetAsync(It.IsAny<InstagramTokenState>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RefreshService_Expired_LogsCritical_NoThrow()
        {
            var provider = new Mock<IInstagramTokenProvider>();
            provider.Setup(p => p.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new InstagramTokenState("tok", expiresOn: null));
            var client = new Mock<IInstagramTokenRefreshClient>();
            client.Setup(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InstagramTokenExpiredException("expired"));
            var logger = new RecordingLogger<InstagramTokenRefreshService>();
            var service = new InstagramTokenRefreshService(provider.Object, client.Object, Config(), new FixedTimeProvider(Now), logger);

            await service.RunOnceAsync(CancellationToken.None);

            provider.Verify(p => p.SetAsync(It.IsAny<InstagramTokenState>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Critical);
        }

        [Fact]
        public async Task RefreshService_NullState_DoesNotAttemptRefresh()
        {
            var provider = new Mock<IInstagramTokenProvider>();
            provider.Setup(p => p.GetStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync((InstagramTokenState)null);
            var client = new Mock<IInstagramTokenRefreshClient>();
            var service = new InstagramTokenRefreshService(provider.Object, client.Object, Config(), new FixedTimeProvider(Now), NullLogger<InstagramTokenRefreshService>.Instance);

            await service.RunOnceAsync(CancellationToken.None);

            client.Verify(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ---- Outbound sender reads the current token at send time -----------

        [Fact]
        public async Task SendMessagesAsync_UsesCurrentTokenFromProvider()
        {
            string url1 = null, url2 = null;
            var call = 0;
            var handler = new StubHttpMessageHandler(request =>
            {
                if (call++ == 0) { url1 = request.RequestUri.ToString(); }
                else { url2 = request.RequestUri.ToString(); }
                return Json(HttpStatusCode.OK, "{}");
            });
            var provider = new Mock<IInstagramTokenProvider>();
            provider.SetupSequence(p => p.GetTokenAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync("TKN1")
                    .ReturnsAsync("TKN2");
            var wrapper = new InstagramClientWrapper(Config(), provider.Object, new HttpClient(handler));
            var requests = new List<InstagramSendRequest>
            {
                new InstagramSendRequest
                {
                    Recipient = new InstagramRecipient { Id = "igsid-1" },
                    Message = new InstagramSendMessage { Text = "hi" },
                    MessagingType = "RESPONSE",
                },
            };

            await wrapper.SendMessagesAsync(requests);
            await wrapper.SendMessagesAsync(requests);

            Assert.Contains("access_token=TKN1", url1);
            Assert.Contains("access_token=TKN2", url2);
            Assert.Contains("graph.instagram.com", url1);
        }

        [Fact]
        public async Task SendMessagesAsync_NonSuccess_ThrowsTypedSendExceptionWithStatus()
        {
            // A dead token returns 401 — the outbound retry must be able to read the status (terminal, no retry)
            // rather than parse a message string. Verifies the InstagramSendException refactor.
            var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":190}}"));
            var provider = new Mock<IInstagramTokenProvider>();
            provider.Setup(p => p.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("dead-token");
            var wrapper = new InstagramClientWrapper(Config(), provider.Object, new HttpClient(handler));
            var requests = new List<InstagramSendRequest>
            {
                new InstagramSendRequest
                {
                    Recipient = new InstagramRecipient { Id = "igsid-1" },
                    Message = new InstagramSendMessage { Text = "hi" },
                    MessagingType = "RESPONSE",
                },
            };

            var ex = await Assert.ThrowsAsync<InstagramSendException>(() => wrapper.SendMessagesAsync(requests));
            Assert.Equal(401, ex.StatusCode);
        }

        [Fact]
        public async Task SendActivitiesAsync_PerActivityRetry_DoesNotResendDeliveredEarlierMessage()
        {
            // A 2-activity reply where the SECOND message hits a transient 429 once: the FIRST (already delivered)
            // must NOT be re-POSTed by the retry. Proves the retry unit is a single activity, not the whole batch.
            var sends = new List<string>();
            var secondAttempts = 0;
            var handler = new StubHttpMessageHandler(request =>
            {
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var which = body.Contains("first") ? "first" : "second";
                sends.Add(which);
                if (which == "second" && ++secondAttempts == 1)
                {
                    return Json(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":4}}");
                }

                return Json(HttpStatusCode.OK, "{}");
            });
            var provider = new Mock<IInstagramTokenProvider>();
            provider.Setup(p => p.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("TKN");
            var adapter = new InstagramAdapter(new InstagramClientWrapper(Config(), provider.Object, new HttpClient(handler)));

            var activities = new List<Activity>
            {
                new Activity { Type = ActivityTypes.Message, Id = "a0", Text = "first", ReplyToId = "igsid-1" },
                new Activity { Type = ActivityTypes.Message, Id = "a1", Text = "second", ReplyToId = "igsid-1" },
            };

            await adapter.SendActivitiesAsync(activities, CancellationToken.None);

            Assert.Equal(1, sends.FindAll(s => s == "first").Count);   // delivered exactly once
            Assert.Equal(2, sends.FindAll(s => s == "second").Count);  // its own transient retry only
        }

        // ---- Test doubles ---------------------------------------------------

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new HttpResponseMessage(status) { Content = new StringContent(body) };

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(_responder(request));
        }

        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _now;

            public FixedTimeProvider(DateTimeOffset now)
            {
                _now = now;
            }

            public override DateTimeOffset GetUtcNow() => _now;
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
