// Copyright (c) Microsoft Corporation. All rights reserved.

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Settings the Instagram adapter needs to talk to the Meta Graph / Instagram messaging APIs.
    /// Bound from the "InstagramAdapterSettings" configuration section. Real values come from
    /// Key Vault / app settings / user-secrets — never commit them (see appsettings.json placeholders).
    /// </summary>
    public class InstagramAdapterConfiguration
    {
        /// <summary>
        /// Meta app secret. Used to validate the inbound X-Hub-Signature-256 header (HMAC-SHA256 of the raw body).
        /// </summary>
        public string AppSecret { get; set; }

        /// <summary>
        /// Token Meta echoes back during the GET webhook handshake. We compare hub.verify_token against this
        /// and reply with hub.challenge when it matches.
        /// </summary>
        public string VerifyToken { get; set; }

        /// <summary>
        /// Long-lived Page/IG access token (System User token preferred) used to call the Send API.
        /// </summary>
        public string PageAccessToken { get; set; }

        /// <summary>
        /// The Instagram-business / Page-scoped id used as the path segment of the Send API call
        /// (POST /{IgBusinessId}/messages). "me" also works when the token is page-scoped.
        /// </summary>
        public string IgBusinessId { get; set; }

        /// <summary>
        /// Graph API version segment, e.g. "v21.0". Optional; defaults applied in the client wrapper.
        /// </summary>
        public string GraphApiVersion { get; set; }

        /// <summary>
        /// When true, outbound replies are tagged messaging_type=MESSAGE_TAG / tag=HUMAN_AGENT, which extends
        /// the reply window to 7 days (vs the 24h service window). Off by default; enable for late agent replies.
        /// </summary>
        public bool UseHumanAgentTag { get; set; }

        /// <summary>
        /// Name of the Key Vault secret holding the Instagram-user token (P1 auto-refresh). Defaults to
        /// "IgUserAccessToken" when unset.
        /// </summary>
        public string TokenSecretName { get; set; }

        /// <summary>
        /// Refresh the token once its remaining lifetime drops below this many days. Defaults to 20 when ≤ 0
        /// (well inside the ~60-day token life).
        /// </summary>
        public int TokenRefreshThresholdDays { get; set; }

        /// <summary>
        /// How often the background refresher checks the token, in hours. Defaults to 12 when ≤ 0.
        /// </summary>
        public int TokenRefreshCheckIntervalHours { get; set; }
    }
}
