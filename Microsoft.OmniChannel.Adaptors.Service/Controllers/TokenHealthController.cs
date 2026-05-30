// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapters.Instagram;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Service.Controllers
{
    /// <summary>
    /// Read-only diagnostic surface for the P1 Instagram token store / Key Vault wiring. Returns NO secret
    /// values — only types, booleans, token length, expiry, and exception text — so a failed Key Vault
    /// auth is visible over HTTP (the App Service log stream is unreliable for this app). Gated behind the
    /// configured VerifyToken so it is not publicly readable.
    /// GET /api/TokenHealth?token=&lt;VerifyToken&gt;
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class TokenHealthController : Controller
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly InstagramAdapterConfiguration _adapterConfig;

        public TokenHealthController(
            IServiceProvider services,
            IConfiguration configuration,
            IOptions<InstagramAdapterConfiguration> adapterConfig)
        {
            _services = services;
            _configuration = configuration;
            _adapterConfig = adapterConfig?.Value ?? new InstagramAdapterConfiguration();
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery(Name = "token")] string token, CancellationToken cancellationToken)
        {
            var verifyToken = _adapterConfig.VerifyToken;
            if (string.IsNullOrEmpty(verifyToken) || !string.Equals(token, verifyToken, StringComparison.Ordinal))
            {
                return StatusCode(403, "Forbidden.");
            }

            var result = new System.Collections.Generic.Dictionary<string, object>
            {
                ["keyVaultUriConfigured"] = !string.IsNullOrWhiteSpace(_configuration["KeyVault:Uri"]),
                ["keyVaultUri"] = _configuration["KeyVault:Uri"] ?? string.Empty,
                ["tokenSecretName"] = _adapterConfig.TokenSecretName ?? string.Empty,
                ["pageAccessTokenConfiguredLength"] = _adapterConfig.PageAccessToken?.Length ?? 0,
            };

            var store = _services.GetService<IInstagramTokenStore>();
            result["storeType"] = store?.GetType().Name ?? "(none)";

            // Probe the durable store directly — this is where a managed-identity / Key Vault auth failure surfaces.
            try
            {
                var state = store == null ? null : await store.GetAsync(cancellationToken).ConfigureAwait(false);
                result["storeGet"] = "ok";
                result["storeHasToken"] = state != null && !string.IsNullOrEmpty(state.Token);
                result["storeTokenLength"] = state?.Token?.Length ?? 0;
                result["storeTokenExpiresOn"] = state?.ExpiresOn?.ToString("o") ?? "(unknown)";
            }
            catch (Exception ex)
            {
                result["storeGet"] = "error";
                result["storeError"] = ex.GetType().Name + ": " + Truncate(ex.Message, 600);
                if (ex.InnerException != null)
                {
                    result["storeInnerError"] = ex.InnerException.GetType().Name + ": " + Truncate(ex.InnerException.Message, 400);
                }
            }

            // Probe the platform managed-identity (MSI) endpoint directly so we can see whether the env vars
            // are injected and exactly what the token endpoint returns. No token value is echoed.
            try
            {
                var idEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
                var idHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
                var imdsEndpoint = Environment.GetEnvironmentVariable("MSI_ENDPOINT");
                result["env_IDENTITY_ENDPOINT_present"] = !string.IsNullOrEmpty(idEndpoint);
                result["env_IDENTITY_HEADER_present"] = !string.IsNullOrEmpty(idHeader);
                result["env_MSI_ENDPOINT_present"] = !string.IsNullOrEmpty(imdsEndpoint);

                if (!string.IsNullOrEmpty(idEndpoint) && !string.IsNullOrEmpty(idHeader))
                {
                    using (var http = new System.Net.Http.HttpClient())
                    {
                        var url = idEndpoint + "?resource=https%3A%2F%2Fvault.azure.net&api-version=2019-08-01";
                        using (var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url))
                        {
                            req.Headers.Add("X-IDENTITY-HEADER", idHeader);
                            using (var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false))
                            {
                                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                                // Redact any access_token value before reporting.
                                var redacted = System.Text.RegularExpressions.Regex.Replace(
                                    body ?? string.Empty,
                                    "\"access_token\"\\s*:\\s*\"[^\"]*\"",
                                    "\"access_token\":\"<redacted>\"");
                                result["msiProbeStatus"] = (int)resp.StatusCode;
                                result["msiProbeBody"] = Truncate(redacted, 500);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result["msiProbeError"] = ex.GetType().Name + ": " + Truncate(ex.Message, 400);
            }

            // Probe the provider (in-memory current token fed to the outbound sender).
            try
            {
                var provider = _services.GetService<IInstagramTokenProvider>();
                var providerState = provider == null ? null : await provider.GetStateAsync(cancellationToken).ConfigureAwait(false);
                result["providerHasToken"] = providerState != null && !string.IsNullOrEmpty(providerState.Token);
                result["providerTokenLength"] = providerState?.Token?.Length ?? 0;
                result["providerTokenExpiresOn"] = providerState?.ExpiresOn?.ToString("o") ?? "(unknown)";
            }
            catch (Exception ex)
            {
                result["providerError"] = ex.GetType().Name + ": " + Truncate(ex.Message, 600);
            }

            return Ok(result);
        }

        private static string Truncate(string value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max) + "…";
    }
}
