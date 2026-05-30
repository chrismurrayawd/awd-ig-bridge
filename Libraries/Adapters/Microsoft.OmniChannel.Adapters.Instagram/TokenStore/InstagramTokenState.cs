// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// The Instagram-user access token plus its known expiry. <see cref="ExpiresOn"/> is null when the
    /// expiry is unknown (e.g. a freshly seeded token) — the refresher treats unknown as "refresh soon".
    /// </summary>
    public class InstagramTokenState
    {
        public InstagramTokenState(string token, DateTimeOffset? expiresOn)
        {
            Token = token;
            ExpiresOn = expiresOn;
        }

        /// <summary>The Instagram-user token (IGAA…) used for the Send API.</summary>
        public string Token { get; }

        /// <summary>When the token expires, or null if not known.</summary>
        public DateTimeOffset? ExpiresOn { get; }
    }
}
