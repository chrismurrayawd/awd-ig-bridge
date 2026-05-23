// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;
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

        private const string GraphApiBaseUrl = "https://graph.facebook.com";
        private const string DefaultGraphApiVersion = "v21.0";

        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly HttpClient _httpClient;

        public InstagramClientWrapper(IOptions<InstagramAdapterConfiguration> configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            if (string.IsNullOrWhiteSpace(_configuration.Value?.PageAccessToken))
            {
                throw new MissingFieldException(nameof(InstagramAdapterConfiguration.PageAccessToken));
            }

            if (string.IsNullOrWhiteSpace(_configuration.Value?.AppSecret))
            {
                throw new MissingFieldException(nameof(InstagramAdapterConfiguration.AppSecret));
            }

            if (string.IsNullOrWhiteSpace(_configuration.Value?.IgBusinessId))
            {
                throw new MissingFieldException(nameof(InstagramAdapterConfiguration.IgBusinessId));
            }

            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Validate the inbound webhook's X-Hub-Signature-256 header against the raw body and app secret.
        /// </summary>
        public bool ValidateSignature(byte[] rawBody, HttpRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var signatureHeader = request.Headers[SignatureHeaderName].ToString();
            return InstagramHelper.IsValidSignature(rawBody, signatureHeader, _configuration.Value.AppSecret);
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

            var version = string.IsNullOrWhiteSpace(_configuration.Value.GraphApiVersion)
                ? DefaultGraphApiVersion
                : _configuration.Value.GraphApiVersion;

            var url = $"{GraphApiBaseUrl}/{version}/{_configuration.Value.IgBusinessId}/messages" +
                      $"?access_token={Uri.EscapeDataString(_configuration.Value.PageAccessToken)}";

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
                        throw new InvalidOperationException(
                            $"Instagram Send API returned {(int)response.StatusCode}: {body}");
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
