// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.Adapters.Instagram;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Microsoft.OmniChannel.Adapters.Instagram.Tests
{
    public class InstagramHelperTests
    {
        private static InstagramWebhookModel MessagePayload(string igsid, string text, bool isEcho = false, InstagramAttachment attachment = null)
        {
            return new InstagramWebhookModel
            {
                Object = "instagram",
                Entry = new List<InstagramEntry>
                {
                    new InstagramEntry
                    {
                        Id = "17841400000000000",
                        Messaging = new List<InstagramMessaging>
                        {
                            new InstagramMessaging
                            {
                                Sender = new InstagramUser { Id = igsid },
                                Recipient = new InstagramUser { Id = "17841400000000000" },
                                Message = new InstagramMessage
                                {
                                    Mid = "mid.abc123",
                                    Text = text,
                                    IsEcho = isEcho,
                                    Attachments = attachment == null ? null : new List<InstagramAttachment> { attachment },
                                },
                            },
                        },
                    },
                },
            };
        }

        [Fact]
        public void PayloadToActivities_MapsTextMessageToActivity()
        {
            var activities = InstagramHelper.PayloadToActivities(MessagePayload("igsid-1", "Hello AWD"));

            var activity = Assert.Single(activities);
            Assert.Equal("igsid-1", activity.From.Id);
            Assert.Equal("Hello AWD", activity.Text);
            Assert.Equal("mid.abc123", activity.Id);
            Assert.Equal(ActivityTypes.Message, activity.Type);
            Assert.Equal(Constant.ChannelId, activity.ChannelId);
            Assert.Equal(Constant.DirectLineBotServiceUrl, activity.ServiceUrl);

            var channelData = Assert.IsType<ActivityExtension>(activity.ChannelData);
            Assert.Equal(ChannelType.Instagram, channelData.ChannelType);
        }

        [Fact]
        public void PayloadToActivities_SkipsEchoMessages()
        {
            var activities = InstagramHelper.PayloadToActivities(MessagePayload("igsid-1", "echoed", isEcho: true));
            Assert.Empty(activities);
        }

        [Fact]
        public void PayloadToActivities_NoEntriesReturnsEmpty()
        {
            var activities = InstagramHelper.PayloadToActivities(new InstagramWebhookModel { Object = "instagram" });
            Assert.Empty(activities);
        }

        [Fact]
        public void PayloadToActivities_AttachmentOnly_ProducesPlaceholderText()
        {
            var attachment = new InstagramAttachment
            {
                Type = "image",
                Payload = new InstagramAttachmentPayload { Url = "https://cdn.example/img.jpg" },
            };

            var activities = InstagramHelper.PayloadToActivities(MessagePayload("igsid-1", null, attachment: attachment));

            var activity = Assert.Single(activities);
            Assert.Contains("[image]", activity.Text);
            Assert.Contains("https://cdn.example/img.jpg", activity.Text);
        }

        [Fact]
        public void ActivityToInstagram_BuildsResponseRequest()
        {
            var activities = new List<Activity> { new Activity { Text = "Sure, happy to help." } };

            var requests = InstagramHelper.ActivityToInstagram(activities, "igsid-1", useHumanAgentTag: false);

            var request = Assert.Single(requests);
            Assert.Equal("igsid-1", request.Recipient.Id);
            Assert.Equal("Sure, happy to help.", request.Message.Text);
            Assert.Equal("RESPONSE", request.MessagingType);
            Assert.Null(request.Tag);
        }

        [Fact]
        public void ActivityToInstagram_HumanAgentTag_SetsTag()
        {
            var activities = new List<Activity> { new Activity { Text = "Late reply" } };

            var requests = InstagramHelper.ActivityToInstagram(activities, "igsid-1", useHumanAgentTag: true);

            var request = Assert.Single(requests);
            Assert.Equal("MESSAGE_TAG", request.MessagingType);
            Assert.Equal("HUMAN_AGENT", request.Tag);
        }

        [Fact]
        public void ActivityToInstagram_SkipsEmptyTextActivities()
        {
            var activities = new List<Activity>
            {
                new Activity { Text = string.Empty },
                new Activity { Text = "kept" },
            };

            var requests = InstagramHelper.ActivityToInstagram(activities, "igsid-1", useHumanAgentTag: false);
            Assert.Single(requests);
        }

        [Fact]
        public void IsValidSignature_ValidSignatureReturnsTrue()
        {
            const string secret = "app-secret-value";
            var body = Encoding.UTF8.GetBytes("{\"object\":\"instagram\"}");
            var header = "sha256=" + ComputeHmacHex(secret, body);

            Assert.True(InstagramHelper.IsValidSignature(body, header, secret));
        }

        [Fact]
        public void IsValidSignature_TamperedBodyReturnsFalse()
        {
            const string secret = "app-secret-value";
            var signedBody = Encoding.UTF8.GetBytes("{\"object\":\"instagram\"}");
            var header = "sha256=" + ComputeHmacHex(secret, signedBody);

            var tamperedBody = Encoding.UTF8.GetBytes("{\"object\":\"instagram\",\"x\":1}");
            Assert.False(InstagramHelper.IsValidSignature(tamperedBody, header, secret));
        }

        [Fact]
        public void IsValidSignature_MissingOrMalformedHeaderReturnsFalse()
        {
            var body = Encoding.UTF8.GetBytes("{}");
            Assert.False(InstagramHelper.IsValidSignature(body, null, "secret"));
            Assert.False(InstagramHelper.IsValidSignature(body, "not-a-signature", "secret"));
        }

        [Fact]
        public void GetVerifiedChallenge_ValidReturnsChallenge()
        {
            var result = InstagramHelper.GetVerifiedChallenge("subscribe", "the-token", "1158201444", "the-token");
            Assert.Equal("1158201444", result);
        }

        [Fact]
        public void GetVerifiedChallenge_WrongTokenReturnsNull()
        {
            var result = InstagramHelper.GetVerifiedChallenge("subscribe", "wrong", "1158201444", "the-token");
            Assert.Null(result);
        }

        [Fact]
        public void GetVerifiedChallenge_WrongModeReturnsNull()
        {
            var result = InstagramHelper.GetVerifiedChallenge("unsubscribe", "the-token", "1158201444", "the-token");
            Assert.Null(result);
        }

        private static string ComputeHmacHex(string secret, byte[] body)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                return Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
            }
        }
    }
}
