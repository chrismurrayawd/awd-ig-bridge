// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.OmniChannel.Adapter.Builder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Maps between the Instagram messaging webhook payload and Bot Framework <see cref="Activity"/> objects,
    /// builds Send API request bodies, and validates Meta's X-Hub-Signature-256 header.
    /// </summary>
    public static class InstagramHelper
    {
        private const string SignaturePrefix = "sha256=";

        /// <summary>
        /// Build inbound Bot Framework activities from an Instagram webhook payload.
        /// One activity per customer message; echoes (the business account's own sends), receipts and
        /// non-message events are skipped. Returns an empty list when there is nothing to relay.
        /// </summary>
        public static IList<Activity> PayloadToActivities(InstagramWebhookModel payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var activities = new List<Activity>();

            if (payload.Entry == null)
            {
                return activities;
            }

            foreach (var entry in payload.Entry)
            {
                if (entry?.Messaging == null)
                {
                    continue;
                }

                foreach (var messaging in entry.Messaging)
                {
                    // Receipts (delivery/read) and non-message events have no message body.
                    if (messaging?.Message == null || messaging.Message.IsEcho)
                    {
                        continue;
                    }

                    var igsid = messaging.Sender?.Id;
                    if (string.IsNullOrEmpty(igsid))
                    {
                        continue;
                    }

                    var text = messaging.Message.Text;

                    // Graceful degrade: stories/mentions/media arrive without text — surface a placeholder
                    // so the agent sees something rather than an empty turn.
                    if (string.IsNullOrEmpty(text))
                    {
                        text = DescribeAttachments(messaging.Message.Attachments);
                    }

                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    var channelData = new ActivityExtension
                    {
                        ChannelType = ChannelType.Instagram,

                        // Routing / contact-match context can be populated here per the D365 workstream design.
                        ConversationContext = new Dictionary<string, string>(),
                        CustomerContext = new Dictionary<string, string>(),
                    };

                    activities.Add(new Activity
                    {
                        From = new ChannelAccount(igsid),
                        ChannelId = Constant.ChannelId,
                        ServiceUrl = Constant.DirectLineBotServiceUrl,
                        Text = text,
                        Type = ActivityTypes.Message,
                        Id = messaging.Message.Mid,
                        ChannelData = channelData,
                    });
                }
            }

            return activities;
        }

        /// <summary>
        /// Convert outbound agent activities into Instagram Send API request bodies addressed to the customer.
        /// </summary>
        /// <param name="activities">Outbound activities from the relay processor.</param>
        /// <param name="recipientId">The customer IGSID to reply to (Activity.ReplyToId).</param>
        /// <param name="useHumanAgentTag">When true, send with the HUMAN_AGENT message tag (7-day window).</param>
        public static List<InstagramSendRequest> ActivityToInstagram(IList<Activity> activities, string recipientId, bool useHumanAgentTag)
        {
            if (string.IsNullOrWhiteSpace(recipientId))
            {
                throw new ArgumentNullException(nameof(recipientId));
            }

            if (activities == null)
            {
                throw new ArgumentNullException(nameof(activities));
            }

            return activities
                .Where(activity => !string.IsNullOrEmpty(activity?.Text))
                .Select(activity => new InstagramSendRequest
                {
                    Recipient = new InstagramRecipient { Id = recipientId },
                    Message = new InstagramSendMessage { Text = activity.Text },
                    MessagingType = useHumanAgentTag ? "MESSAGE_TAG" : "RESPONSE",
                    Tag = useHumanAgentTag ? "HUMAN_AGENT" : null,
                })
                .ToList();
        }

        /// <summary>
        /// Validate Meta's X-Hub-Signature-256 header: HMAC-SHA256 of the raw request body keyed by the app secret.
        /// </summary>
        /// <param name="rawBody">The exact bytes of the request body as received.</param>
        /// <param name="signatureHeader">The X-Hub-Signature-256 header value (e.g. "sha256=ab12...").</param>
        /// <param name="appSecret">The Meta app secret.</param>
        /// <returns>True when the signature matches.</returns>
        public static bool IsValidSignature(byte[] rawBody, string signatureHeader, string appSecret)
        {
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                throw new ArgumentNullException(nameof(appSecret));
            }

            if (rawBody == null)
            {
                throw new ArgumentNullException(nameof(rawBody));
            }

            if (string.IsNullOrWhiteSpace(signatureHeader) ||
                !signatureHeader.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var provided = signatureHeader.Substring(SignaturePrefix.Length).ToLowerInvariant();

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret)))
            {
                var computed = ToHex(hmac.ComputeHash(rawBody));

                if (provided.Length != computed.Length)
                {
                    return false;
                }

                return CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(provided),
                    Encoding.ASCII.GetBytes(computed));
            }
        }

        /// <summary>
        /// Verify Meta's GET webhook handshake. Returns the hub.challenge to echo back when the mode is
        /// "subscribe" and hub.verify_token matches the configured token; otherwise null.
        /// </summary>
        public static string GetVerifiedChallenge(string mode, string verifyToken, string challenge, string configuredVerifyToken)
        {
            if (string.IsNullOrEmpty(configuredVerifyToken))
            {
                return null;
            }

            var isSubscribe = string.Equals(mode, "subscribe", StringComparison.Ordinal);
            var tokenMatches = string.Equals(verifyToken, configuredVerifyToken, StringComparison.Ordinal);

            return isSubscribe && tokenMatches ? challenge : null;
        }

        private static string DescribeAttachments(IList<InstagramAttachment> attachments)
        {
            if (attachments == null || attachments.Count == 0)
            {
                return null;
            }

            var parts = attachments.Select(a =>
            {
                var type = string.IsNullOrEmpty(a?.Type) ? "attachment" : a.Type;
                var url = a?.Payload?.Url;
                return string.IsNullOrEmpty(url) ? $"[{type}]" : $"[{type}] {url}";
            });

            return string.Join(" ", parts);
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
