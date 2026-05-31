// Copyright (c) Microsoft Corporation. All rights reserved.

using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.Adapters.Instagram;
using Microsoft.OmniChannel.MessageRelayProcessor;
using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using Newtonsoft.Json.Serialization;
using NLog.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Microsoft.OmniChannel.Adapters.Service
{
    /// <summary>
    /// Application entry point. Migrated from the sample's ASP.NET Core 2.1 Startup/Program
    /// pair to the net8.0 minimal hosting model; service registration and the request
    /// pipeline are unchanged in behaviour.
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Logging: route through NLog (nlog.config -> stdout console), as the sample did.
            builder.Logging.ClearProviders();
            builder.Logging.AddNLog();

            // P2 observability: Application Insights. No-op when APPLICATIONINSIGHTS_CONNECTION_STRING is
            // unset (local/dev), so this is safe to leave in regardless of environment. When set, ILogger
            // entries (Information+) and request telemetry flow to a queryable sink — the App Service docker
            // log stream has proven unreliable for this app.
            builder.Services.AddApplicationInsightsTelemetry();
            builder.Logging.AddApplicationInsights();

            ConfigureServices(builder.Services, builder.Configuration);

            var app = builder.Build();

            app.MapControllers();

            var defaultFilesOptions = new DefaultFilesOptions();
            defaultFilesOptions.DefaultFileNames.Clear();
            defaultFilesOptions.DefaultFileNames.Add("default.htm");
            app.UseDefaultFiles(defaultFilesOptions);
            app.UseStaticFiles();

            app.Run();
        }

        // Registers adapters, the relay processor, and the channel resolver.
        // Public so it can be exercised from tests if needed.
        public static void ConfigureServices(IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            services.AddControllers()
                .AddNewtonsoftJson(options =>
                {
                    // Preserve the property names as declared (no camelCasing) — Direct Line / channel payloads are case-sensitive.
                    if (options.SerializerSettings.ContractResolver is DefaultContractResolver resolver)
                    {
                        resolver.NamingStrategy = null;
                    }
                });

            services.Configure<InstagramAdapterConfiguration>(configuration.GetSection("InstagramAdapterSettings"));
            services.Configure<RelayProcessorConfiguration>(configuration.GetSection("RelayProcessorSettings"));

            services.AddSingleton<InstagramAdapter>();
            // Stateless + durable (P3): singleton, sharing the conversation store and the Direct Line gateway.
            services.AddSingleton<IRelayProcessor, RelayProcessor>();

            // P3 — Durable conversation store (plan step 8). Table Storage when RelayProcessorSettings:TableServiceUri
            // is set (production, via the same managed identity as the Key Vault); in-memory fallback otherwise so
            // local dev / tests need no Azure (mirrors the KeyVault:Uri switch below).
            var tableServiceUri = configuration["RelayProcessorSettings:TableServiceUri"];
            if (!string.IsNullOrWhiteSpace(tableServiceUri))
            {
                var tableName = configuration["RelayProcessorSettings:ConversationsTableName"];
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    tableName = "Conversations";
                }

                services.AddSingleton<ITableClientAdapter>(serviceProvider =>
                {
                    var serviceClient = new TableServiceClient(new Uri(tableServiceUri), new DefaultAzureCredential());
                    var tableClient = serviceClient.GetTableClient(tableName);
                    try
                    {
                        tableClient.CreateIfNotExists();
                    }
                    catch (Exception ex)
                    {
                        // This runs eagerly during host startup (the poller hosted service resolves the store). A raw
                        // throw here takes the whole bridge offline with no signal — emit a loud, actionable cause
                        // first (the most likely culprit is the missing managed-identity role), then fail fast.
                        serviceProvider.GetService<ILoggerFactory>()?
                            .CreateLogger("ConversationStore")
                            .LogCritical(ex,
                                "Failed to provision the conversations table '{Table}' at {Uri}. Verify the App Service managed identity has the 'Storage Table Data Contributor' role on the storage account.",
                                tableName, tableServiceUri);
                        throw;
                    }

                    return new TableClientAdapter(tableClient);
                });
                services.AddSingleton<IConversationStore, TableConversationStore>();
            }
            else
            {
                services.AddSingleton<IConversationStore, InMemoryConversationStore>();
            }

            // One shared Direct Line gateway (bound only to the secret, so it serves every conversation), wrapped in
            // retry/backoff. The factory is the single place the secret is consumed (eases the future P5 KV move).
            services.AddSingleton<IDirectLineGatewayFactory>(serviceProvider =>
                new ResilientDirectLineGatewayFactory(
                    new DirectLineGatewayFactory(serviceProvider.GetRequiredService<IOptions<RelayProcessorConfiguration>>()),
                    serviceProvider.GetRequiredService<ILogger<ResilientDirectLineGateway>>()));
            services.AddSingleton<IDirectLineGateway>(serviceProvider =>
                serviceProvider.GetRequiredService<IDirectLineGatewayFactory>().Create());

            // Outbound reply delivery resolved by channel (replaces the per-call closure), mirroring AdapterServiceResolver.
            services.AddSingleton<OutboundSinkResolver>(serviceProvider => key => key switch
            {
                ChannelType.Instagram => serviceProvider.GetRequiredService<InstagramAdapter>(),
                _ => throw new KeyNotFoundException(key),
            });

            // The single background poller that rehydrates Active conversations and delivers agent replies.
            services.AddHostedService<ConversationPollingService>();

            // P1 — Instagram-user token persistence + auto-refresh (plan step 8).
            // Key Vault store when KeyVault:Uri is set (production, via managed identity); config-backed
            // fallback otherwise (local dev / tests need no Azure).
            services.AddSingleton(TimeProvider.System);

            var keyVaultUri = configuration["KeyVault:Uri"];
            if (!string.IsNullOrWhiteSpace(keyVaultUri))
            {
                services.AddSingleton<ISecretClientAdapter>(_ => new KeyVaultSecretClientAdapter(new Uri(keyVaultUri)));
                services.AddSingleton<IInstagramTokenStore, KeyVaultInstagramTokenStore>();
                // P5 — Meta app secret durable in Key Vault (reuses the same ISecretClientAdapter as the token).
                services.AddSingleton<IAppSecretStore, KeyVaultAppSecretStore>();
            }
            else
            {
                services.AddSingleton<IInstagramTokenStore, ConfigInstagramTokenStore>();
                services.AddSingleton<IAppSecretStore, ConfigAppSecretStore>();
            }

            services.AddSingleton<IInstagramTokenProvider, InstagramTokenProvider>();
            services.AddHttpClient<IInstagramTokenRefreshClient, InstagramTokenRefreshClient>();
            services.AddHostedService<InstagramTokenRefreshService>();

            // P5 — Meta app secret provider (load-once + in-memory cache + auto-seed; no refresher — it never expires).
            services.AddSingleton<IAppSecretProvider, AppSecretProvider>();

            services.AddSingleton<AdapterServiceResolver>(serviceProvider => key =>
            {
                switch (key)
                {
                    case ChannelType.Instagram:
                        return serviceProvider.GetService<InstagramAdapter>();
                    default:
                        throw new KeyNotFoundException();
                }
            });
        }
    }
}
