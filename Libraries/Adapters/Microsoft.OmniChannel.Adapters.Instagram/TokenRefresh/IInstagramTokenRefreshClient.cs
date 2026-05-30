// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Calls the Instagram long-lived-token refresh endpoint
    /// (<c>GET graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token</c>).
    /// </summary>
    public interface IInstagramTokenRefreshClient
    {
        /// <summary>
        /// Exchanges the current token for a fresh ~60-day token. Throws
        /// <see cref="InstagramTokenTooFreshException"/> when the token is &lt;24h old and
        /// <see cref="InstagramTokenExpiredException"/> when it is expired/invalid.
        /// </summary>
        Task<InstagramTokenRefreshResult> RefreshAsync(string currentToken, CancellationToken cancellationToken);
    }

    /// <summary>The result of a successful refresh: the new token and its computed expiry.</summary>
    public class InstagramTokenRefreshResult
    {
        public InstagramTokenRefreshResult(string token, DateTimeOffset expiresOn)
        {
            Token = token;
            ExpiresOn = expiresOn;
        }

        public string Token { get; }

        public DateTimeOffset ExpiresOn { get; }
    }
}
