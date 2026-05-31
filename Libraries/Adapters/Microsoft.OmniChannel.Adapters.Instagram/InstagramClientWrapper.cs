// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Wraps the Meta Graph / Instagram messaging HTTP surface: inbound signature validation and
    /// the outbound Send API call.
    /// </summary>
    public class InstagramClientWrapper : IDisposable
    {
        /// <summary>X-Hub-Signature-256 request header carrying the HMAC of the body.</summary>
        public const string SignatureHeaderName = "X-Hub-Signature-256";

        // Instagram API with Instagram Login signs webhooks with the Instagram app secret and serves the
        // Send API from graph.instagram.com (NOT graph.facebook.com — that returns "(#3) Application does not
        // have the capability"). The access token must be an Instagram-user token (IGAA…), not a Page token.
        private const string GraphApiBaseUrl = "https://graph.instagram.com";
        private const string DefaultGraphApiVersion = "v21.0";

        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly IInstagramTokenProvider _tokenProvider;
        private readonly IAppSecretProvider _appSecretProvider;
        private readonly HttpClient _httpClient;

        public InstagramClientWrapper(IOptions<InstagramAdapterConfiguration> configuration, IInstagramTokenProvider tokenProvider, IAppSecretProvider appSecretProvider)
            : this(configuration, tokenProvider, appSecretProvider, new HttpClient())
        {
        }

        // Overload taking an explicit HttpClient — used by tests to inject a stub handler.
        public InstagramClientWrapper(IOptions<InstagramAdapterConfiguration> configuration, IInstagramTokenProvider tokenProvider, IAppSecretProvider appSecretProvider, HttpClient httpClient)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
            _appSecretProvider = appSecretProvider ?? throw new ArgumentNullException(nameof(appSecretProvider));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Neither the access token nor the app secret is validated here: both are owned by their providers
            // (which may still be initialising / seeding at construction time). IgBusinessId is static config.
            if (string.IsNullOrWhiteSpace(_configuration.Value?.IgBusinessId))
            {
                throw new MissingFieldException(nameof(InstagramAdapterConfiguration.IgBusinessId));
            }
        }

        // Backwards-compatible overload (config-backed app secret) so existing tests that build the wrapper with
        // just a token provider + HttpClient compile unchanged. Production resolves the real provider via DI.
        public InstagramClientWrapper(IOptions<InstagramAdapterConfiguration> configuration, IInstagramTokenProvider tokenProvider, HttpClient httpClient)
            : this(
                  configuration,
                  tokenProvider,
                  new AppSecretProvider(
                      new ConfigAppSecretStore(configuration, NullLogger<ConfigAppSecretStore>.Instance),
                      configuration,
                      NullLogger<AppSecretProvider>.Instance),
                  httpClient)
        {
        }

        /// <summary>
        /// Validate the inbound webhook's X-Hub-Signature-256 header against the raw body and the app secret read
        /// from the provider (Key Vault-backed in production, cached in memory). Async so the secret can be loaded
        /// from the store on first use; every subsequent call is an in-memory cache hit.
        /// </summary>
        public async Task<bool> ValidateSignatureAsync(byte[] rawBody, HttpRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var signatureHeader = request.Headers[SignatureHeaderName].ToString();
            var appSecret = await _appSecretProvider.GetAppSecretAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                // No app secret available (Key Vault empty + app setting absent) — we cannot validate, so fail
                // closed (the caller turns this into a 403), never throw. The provider has already logged why.
                return false;
            }

            return InstagramHelper.IsValidSignature(rawBody, signatureHeader, appSecret);
        }

        /// <summary>
        /// POST each reply to the Instagram Send API (/{IgBusinessId}/messages).
        /// </summary>
        public async Task SendMessagesAsync(IList<InstagramSendRequest> sendRequests)
        {
            if (sendRequests == null)
            {
                throw new ArgumentNullException(nameof(sendRequests));
            }

            // Read the token from the provider at send time so a background refresh takes effect with no restart.
            var token = await _tokenProvider.GetTokenAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("No Instagram access token is available to send the outbound message.");
            }

            var version = string.IsNullOrWhiteSpace(_configuration.Value.GraphApiVersion)
                ? DefaultGraphApiVersion
                : _configuration.Value.GraphApiVersion;

            var url = $"{GraphApiBaseUrl}/{version}/{_configuration.Value.IgBusinessId}/messages" +
                      $"?access_token={Uri.EscapeDataString(token)}";

            foreach (var sendRequest in sendRequests)
            {
                using (var message = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    var content = JsonConvert.SerializeObject(sendRequest);
                    message.Content = new StringContent(content, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(message).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        // Typed so the outbound retry can classify transient (5xx/429) vs terminal (4xx auth) by
                        // status instead of parsing this message — a dead token (401/190) must fail loudly, not retry.
                        throw new InstagramSendException((int)response.StatusCode, body);
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient.Dispose();
            }
        }
    }
}
