// Copyright (c) Microsoft Corporation. All rights reserved.

using Newtonsoft.Json;
using System.Collections.Generic;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Inbound Instagram messaging webhook envelope.
    /// Shape: { "object": "instagram", "entry": [ { "messaging": [ { "sender", "message", ... } ] } ] }
    /// (DMs ride on the Messenger Platform "messaging" array; comments/mentions use "changes" and are ignored here.)
    /// </summary>
    public class InstagramWebhookModel
    {
        [JsonProperty("object")]
        public string Object { get; set; }

        [JsonProperty("entry")]
        public IList<InstagramEntry> Entry { get; set; }
    }

    public class InstagramEntry
    {
        /// <summary>The IG business account id that received the event.</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("time")]
        public long Time { get; set; }

        [JsonProperty("messaging")]
        public IList<InstagramMessaging> Messaging { get; set; }
    }

    public class InstagramMessaging
    {
        /// <summary>The customer (sender). sender.id is the IGSID we key the conversation on.</summary>
        [JsonProperty("sender")]
        public InstagramUser Sender { get; set; }

        /// <summary>The IG business account (recipient).</summary>
        [JsonProperty("recipient")]
        public InstagramUser Recipient { get; set; }

        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        [JsonProperty("message")]
        public InstagramMessage Message { get; set; }
    }

    public class InstagramUser
    {
        [JsonProperty("id")]
        public string Id { get; set; }
    }

    public class InstagramMessage
    {
        /// <summary>Message id (used as the Activity Id).</summary>
        [JsonProperty("mid")]
        public string Mid { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        /// <summary>True for echoes of messages the business account itself sent — these must be skipped.</summary>
        [JsonProperty("is_echo")]
        public bool IsEcho { get; set; }

        /// <summary>Media/share/story attachments. Mapped to a text placeholder in dev-grade.</summary>
        [JsonProperty("attachments")]
        public IList<InstagramAttachment> Attachments { get; set; }
    }

    public class InstagramAttachment
    {
        /// <summary>e.g. image, video, audio, file, share, story_mention.</summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("payload")]
        public InstagramAttachmentPayload Payload { get; set; }
    }

    public class InstagramAttachmentPayload
    {
        [JsonProperty("url")]
        public string Url { get; set; }
    }
}
