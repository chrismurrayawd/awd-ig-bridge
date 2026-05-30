// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Default <see cref="IInstagramTokenRefreshClient"/> against graph.instagram.com. Each successful refresh
    /// returns a new ~60-day token (sliding window), so refreshing well before expiry keeps the token alive.
    /// </summary>
    public class InstagramTokenRefreshClient : IInstagramTokenRefreshClient
    {
        private const string GraphApiBaseUrl = "https://graph.instagram.com";

        private readonly HttpClient _httpClient;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<InstagramTokenRefreshClient> _logger;

        public InstagramTokenRefreshClient(
            HttpClient httpClient,
            TimeProvider timeProvider,
            ILogger<InstagramTokenRefreshClient> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<InstagramTokenRefreshResult> RefreshAsync(string currentToken, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(currentToken))
            {
                throw new ArgumentException("A current token is required to refresh.", nameof(currentToken));
            }

            var url = $"{GraphApiBaseUrl}/refresh_access_token?grant_type=ig_refresh_token" +
                      $"&access_token={Uri.EscapeDataString(currentToken)}";

            using (var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw MapError(response.StatusCode, body);
                }

                RefreshResponse parsed;
                try
                {
                    parsed = JsonConvert.DeserializeObject<RefreshResponse>(body);
                }
                catch (JsonException)
                {
                    parsed = null;
                }

                if (parsed == null || string.IsNullOrWhiteSpace(parsed.AccessToken))
                {
                    // Never include the body here: on a 2xx response it contains the freshly minted token.
                    throw new InvalidOperationException(
                        $"Instagram token refresh returned a {(int)response.StatusCode} response with no parseable access_token (body length {body?.Length ?? 0}).");
                }

                var expiresOn = _timeProvider.GetUtcNow().AddSeconds(parsed.ExpiresIn);
                _logger.LogInformation("Instagram token refreshed via graph.instagram.com; new expiry {Expiry}.", expiresOn);
                return new InstagramTokenRefreshResult(parsed.AccessToken, expiresOn);
            }
        }

        private static Exception MapError(HttpStatusCode status, string body)
        {
            string message = body;
            int? code = null;

            try
            {
                var envelope = JsonConvert.DeserializeObject<ErrorEnvelope>(body);
                if (envelope?.Error != null)
                {
                    message = string.IsNullOrEmpty(envelope.Error.Message) ? body : envelope.Error.Message;
                    code = envelope.Error.Code;
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body — fall back to the raw text.
            }

            if (!string.IsNullOrEmpty(message) &&
                message.IndexOf("24 hour", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new InstagramTokenTooFreshException(
                    $"Instagram token cannot be refreshed yet (must be at least 24h old): {message}");
            }

            if (code == 190)
            {
                return new InstagramTokenExpiredException(
                    $"Instagram token is expired or invalid (Graph code 190): {message}");
            }

            return new InvalidOperationException($"Instagram token refresh failed ({(int)status}): {message}");
        }

        private class RefreshResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("token_type")]
            public string TokenType { get; set; }

            [JsonProperty("expires_in")]
            public long ExpiresIn { get; set; }
        }

        private class ErrorEnvelope
        {
            [JsonProperty("error")]
            public ErrorBody Error { get; set; }
        }

        private class ErrorBody
        {
            [JsonProperty("message")]
            public string Message { get; set; }

            [JsonProperty("code")]
            public int Code { get; set; }

            [JsonProperty("error_subcode")]
            public int ErrorSubcode { get; set; }
        }
    }
}
