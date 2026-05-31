// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.Adapters.Instagram;
using Microsoft.OmniChannel.Adapters.Service;
using Microsoft.OmniChannel.MessageRelayProcessor;
using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.OmniChannel.Adapters.Service.Tests
{
    /// <summary>
    /// Smoke-tests Program.ConfigureServices so the full P3 DI graph is proven to resolve at startup — this catches
    /// missing registrations / captive dependencies that compile cleanly but would only fail when the app boots.
    /// Uses the no-Azure fallbacks (no TableServiceUri, no KeyVault:Uri).
    /// </summary>
    public class DependencyInjectionTests
    {
        private static ServiceProvider BuildProvider()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["InstagramAdapterSettings:AppSecret"] = "app-secret",
                    ["InstagramAdapterSettings:IgBusinessId"] = "ig-business-1",
                    ["InstagramAdapterSettings:PageAccessToken"] = "seed-token",
                    ["RelayProcessorSettings:DirectLineSecret"] = "dl-secret",
                    ["RelayProcessorSettings:BotHandle"] = "awd-instagram-bot",
                    ["RelayProcessorSettings:PollingIntervalInMilliseconds"] = "2000",
                    // No TableServiceUri / KeyVault:Uri → in-memory + config fallbacks (no Azure).
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            Program.ConfigureServices(services, configuration);
            return services.BuildServiceProvider();
        }

        [Fact]
        public void ConfigureServices_ResolvesDurableConversationGraph()
        {
            using var provider = BuildProvider();

            Assert.IsType<InMemoryConversationStore>(provider.GetRequiredService<IConversationStore>());
            Assert.NotNull(provider.GetRequiredService<IDirectLineGateway>());
            Assert.NotNull(provider.GetRequiredService<IRelayProcessor>());
            Assert.NotNull(provider.GetRequiredService<InstagramAdapter>());
        }

        [Fact]
        public void ConfigureServices_OutboundSinkResolves_Instagram()
        {
            using var provider = BuildProvider();

            var resolver = provider.GetRequiredService<OutboundSinkResolver>();
            var sink = resolver(ChannelType.Instagram);

            Assert.NotNull(sink);
            Assert.Same(provider.GetRequiredService<InstagramAdapter>(), sink);
        }

        [Fact]
        public void ConfigureServices_RegistersPollingService_AsHostedService()
        {
            using var provider = BuildProvider();

            var hosted = provider.GetServices<IHostedService>().ToList();

            Assert.Contains(hosted, h => h is ConversationPollingService);
        }

        [Fact]
        public void ConfigureServices_ResolvesSecretProviders_AsConfigFallback_NoAzure()
        {
            using var provider = BuildProvider();

            // P5 — no KeyVault:Uri, so both secrets fall back to config (no Azure, no warming).
            Assert.NotNull(provider.GetRequiredService<IAppSecretProvider>());
            Assert.IsType<ConfigAppSecretStore>(provider.GetRequiredService<IAppSecretStore>());
            Assert.IsType<ConfigDirectLineSecretProvider>(provider.GetRequiredService<IDirectLineSecretProvider>());

            // Regression guard: the gateway still resolves via the swapped factory secret source with no warming.
            Assert.NotNull(provider.GetRequiredService<IDirectLineGateway>());
        }
    }
}
