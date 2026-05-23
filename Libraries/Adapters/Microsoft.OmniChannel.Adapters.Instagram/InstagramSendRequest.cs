// Copyright (c) Microsoft Corporation. All rights reserved.

using Newtonsoft.Json;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Body for the Instagram Send API call (POST /{IgBusinessId}/messages).
    /// </summary>
    public class InstagramSendRequest
    {
        [JsonProperty("recipient")]
        public InstagramRecipient Recipient { get; set; }

        [JsonProperty("message")]
        public InstagramSendMessage Message { get; set; }

        /// <summary>"RESPONSE" within the 24h service window; "MESSAGE_TAG" when sending with a tag.</summary>
        [JsonProperty("messaging_type")]
        public string MessagingType { get; set; }

        /// <summary>e.g. "HUMAN_AGENT" to extend the reply window to 7 days. Omitted when null.</summary>
        [JsonProperty("tag", NullValueHandling = NullValueHandling.Ignore)]
        public string Tag { get; set; }
    }

    public class InstagramRecipient
    {
        [JsonProperty("id")]
        public string Id { get; set; }
    }

    public class InstagramSendMessage
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
