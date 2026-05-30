// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Pure decision for whether the token is due for a refresh — kept separate from I/O so it is trivially testable.
    /// </summary>
    public static class InstagramTokenRefreshPolicy
    {
        private const int DefaultThresholdDays = 20;

        /// <summary>
        /// Refresh when there is a token AND (its expiry is unknown — refresh eagerly to learn it — OR its
        /// remaining lifetime is within the threshold). Returns false when there is no token to refresh.
        /// </summary>
        public static bool ShouldRefresh(InstagramTokenState state, DateTimeOffset utcNow, int thresholdDays)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Token))
            {
                return false;
            }

            if (state.ExpiresOn == null)
            {
                return true;
            }

            var days = thresholdDays <= 0 ? DefaultThresholdDays : thresholdDays;
            return state.ExpiresOn.Value - utcNow <= TimeSpan.FromDays(days);
        }
    }
}
