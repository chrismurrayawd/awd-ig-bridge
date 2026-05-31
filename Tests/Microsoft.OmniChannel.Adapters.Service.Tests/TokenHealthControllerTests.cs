// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapters.Instagram;
using Microsoft.OmniChannel.Adapters.Service.Controllers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.Adapters.Service.Tests
{
    /// <summary>
    /// P5 — the gated TokenHealth diagnostic reports the two new secrets' store type + load result (length / type /
    /// booleans only) for deploy verification. These tests prove it stays gated and NEVER emits a secret value.
    /// </summary>
    public class TokenHealthControllerTests
    {
        private const string VerifyToken = "verify-token";
        private const string AppSecretValue = "APPSECRET_VALUE_MUST_NOT_LEAK";
        private const string DirectLineSecretValue = "DLSECRET_VALUE_MUST_NOT_LEAK";

        private static (TokenHealthController Controller, ServiceProvider Provider) Build()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["InstagramAdapterSettings:VerifyToken"] = VerifyToken,
                    ["InstagramAdapterSettings:AppSecret"] = AppSecretValue,
                    ["InstagramAdapterSettings:IgBusinessId"] = "ig-business-1",
                    ["InstagramAdapterSettings:PageAccessToken"] = "seed-token",
                    ["RelayProcessorSettings:DirectLineSecret"] = DirectLineSecretValue,
                    ["RelayProcessorSettings:BotHandle"] = "awd-instagram-bot",
                    ["RelayProcessorSettings:PollingIntervalInMilliseconds"] = "2000",
                    // No KeyVault:Uri / TableServiceUri → config + in-memory fallbacks (no Azure).
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            Program.ConfigureServices(services, configuration);
            var provider = services.BuildServiceProvider();

            var controller = new TokenHealthController(
                provider, configuration, provider.GetRequiredService<IOptions<InstagramAdapterConfiguration>>());
            return (controller, provider);
        }

        [Fact]
        public async Task Get_WrongToken_Returns403()
        {
            var (controller, provider) = Build();
            using (provider)
            {
                var result = await controller.Get("wrong-token", CancellationToken.None);

                var objectResult = Assert.IsType<ObjectResult>(result);
                Assert.Equal(403, objectResult.StatusCode);
            }
        }

        [Fact]
        public async Task Get_RightToken_ReportsNewSecretFields_WithoutLeakingValues()
        {
            var (controller, provider) = Build();
            using (provider)
            {
                var result = await controller.Get(VerifyToken, CancellationToken.None);

                var ok = Assert.IsType<OkObjectResult>(result);
                var dict = Assert.IsType<Dictionary<string, object>>(ok.Value);

                Assert.Equal("ConfigAppSecretStore", dict["appSecretStoreType"]);
                Assert.Equal("ConfigDirectLineSecretProvider", dict["directLineSecretProviderType"]);
                Assert.True((bool)dict["appSecretProviderHasValue"]);
                Assert.True((bool)dict["directLineSecretHasValue"]);
                Assert.Equal(AppSecretValue.Length, (int)dict["appSecretProviderLength"]);
                Assert.Equal(DirectLineSecretValue.Length, (int)dict["directLineSecretLength"]);

                // No secret VALUE may appear anywhere in the diagnostic payload — lengths/types/booleans only.
                foreach (var entry in dict)
                {
                    var text = entry.Value?.ToString() ?? string.Empty;
                    Assert.DoesNotContain(AppSecretValue, text);
                    Assert.DoesNotContain(DirectLineSecretValue, text);
                }
            }
        }
    }
}
