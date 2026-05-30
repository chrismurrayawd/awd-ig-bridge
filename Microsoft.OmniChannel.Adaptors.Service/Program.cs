// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.Adapters.Instagram;
using Microsoft.OmniChannel.Adapters.Line;
using Microsoft.OmniChannel.MessageRelayProcessor;
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

            services.Configure<LineAdapterConfiguration>(configuration.GetSection("LineAdapterSettings"));
            services.Configure<InstagramAdapterConfiguration>(configuration.GetSection("InstagramAdapterSettings"));
            services.Configure<RelayProcessorConfiguration>(configuration.GetSection("RelayProcessorSettings"));

            services.AddSingleton<LineAdapter>();
            services.AddSingleton<InstagramAdapter>();
            services.AddTransient<IRelayProcessor, RelayProcessor>();

            // P1 — Instagram-user token persistence + auto-refresh (plan step 8).
            // Key Vault store when KeyVault:Uri is set (production, via managed identity); config-backed
            // fallback otherwise (local dev / tests need no Azure).
            services.AddSingleton(TimeProvider.System);

            var keyVaultUri = configuration["KeyVault:Uri"];
            if (!string.IsNullOrWhiteSpace(keyVaultUri))
            {
                services.AddSingleton<ISecretClientAdapter>(_ => new KeyVaultSecretClientAdapter(new Uri(keyVaultUri)));
                services.AddSingleton<IInstagramTokenStore, KeyVaultInstagramTokenStore>();
            }
            else
            {
                services.AddSingleton<IInstagramTokenStore, ConfigInstagramTokenStore>();
            }

            services.AddSingleton<IInstagramTokenProvider, InstagramTokenProvider>();
            services.AddHttpClient<IInstagramTokenRefreshClient, InstagramTokenRefreshClient>();
            services.AddHostedService<InstagramTokenRefreshService>();

            services.AddSingleton<AdapterServiceResolver>(serviceProvider => key =>
            {
                switch (key)
                {
                    case ChannelType.Line:
                        return serviceProvider.GetService<LineAdapter>();
                    case ChannelType.Instagram:
                        return serviceProvider.GetService<InstagramAdapter>();
                    default:
                        throw new KeyNotFoundException();
                }
            });
        }
    }
}
