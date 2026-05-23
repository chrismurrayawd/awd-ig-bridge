// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.Adapters.Line;
using Microsoft.OmniChannel.Adapters.MessageBird;
using Microsoft.OmniChannel.MessageRelayProcessor;
using Newtonsoft.Json.Serialization;
using NLog.Extensions.Logging;
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

            // Logging: route through NLog (nlog.config), as the sample did.
            builder.Logging.ClearProviders();
            builder.Logging.AddNLog();

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
            services.Configure<MessageBirdAdapterConfiguration>(configuration.GetSection("MessageBirdAdapterSettings"));
            services.Configure<RelayProcessorConfiguration>(configuration.GetSection("RelayProcessorSettings"));

            services.AddSingleton<LineAdapter>();
            services.AddSingleton<MessageBirdAdapter>();
            services.AddTransient<IRelayProcessor, RelayProcessor>();

            services.AddSingleton<AdapterServiceResolver>(serviceProvider => key =>
            {
                switch (key)
                {
                    case ChannelType.Line:
                        return serviceProvider.GetService<LineAdapter>();
                    case ChannelType.MessageBird:
                        return serviceProvider.GetService<MessageBirdAdapter>();
                    default:
                        throw new KeyNotFoundException();
                }
            });
        }
    }
}
