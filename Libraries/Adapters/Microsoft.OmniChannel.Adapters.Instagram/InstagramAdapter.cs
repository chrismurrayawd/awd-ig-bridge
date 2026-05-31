// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.MessageRelayProcessor;
using Microsoft.OmniChannel.MessageRelayProcessor.Resilience;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Processes inbound and outbound Instagram Direct messages.
    /// Inbound: validate the Meta signature, map the webhook payload to Bot Framework activities, hand to the relay.
    /// Outbound: convert agent reply activities into Instagram Send API calls. Implements
    /// <see cref="IOutboundActivitySink"/> so the polling service can deliver replies resolved from DI by channel —
    /// no per-call closure, so replies survive a restart.
    /// </summary>
    public class InstagramAdapter : IAdapterBuilder, IOutboundActivitySink
    {
        private readonly IRelayProcessor _relayProcessor;
        private readonly InstagramClientWrapper _instagramClient;
        private readonly bool _useHumanAgentTag;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstagramAdapter"/> class using configuration settings.
        /// </summary>
        public InstagramAdapter(IRelayProcessor relayProcessor, IOptions<InstagramAdapterConfiguration> instagramAdapterConfiguration, IInstagramTokenProvider tokenProvider, IAppSecretProvider appSecretProvider, ILogger<InstagramAdapter> logger = null)
            : this(new InstagramClientWrapper(instagramAdapterConfiguration, tokenProvider, appSecretProvider))
        {
            _relayProcessor = relayProcessor ?? throw new ArgumentNullException(nameof(relayProcessor));
            _useHumanAgentTag = instagramAdapterConfiguration?.Value?.UseHumanAgentTag ?? false;
            _logger = logger;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InstagramAdapter"/> class with an explicit client (used by tests).
        /// </summary>
        public InstagramAdapter(InstagramClientWrapper instagramClient)
        {
            _instagramClient = instagramClient ?? throw new ArgumentNullException(nameof(instagramClient));
        }

        /// <summary>
        /// Validate the Meta signature over the raw body, map the payload to activities, and post them to the relay.
        /// Relies on the controller having enabled request buffering so the raw body can be re-read here.
        /// </summary>
        public async Task ProcessInboundActivitiesAsync(JToken content, HttpRequest request)
        {
            if (content == null || content.Type == JTokenType.Null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var rawBody = await ReadRawBodyAsync(request).ConfigureAwait(false);

            if (!await _instagramClient.ValidateSignatureAsync(rawBody, request).ConfigureAwait(false))
            {
                throw new InvalidOperationException(Constant.InvalidSignatureExceptionMessage);
            }

            var payload = content.ToObject<InstagramWebhookModel>();
            var activities = InstagramHelper.PayloadToActivities(payload);

            foreach (var activity in activities)
            {
                // No callback: the relay persists the conversation; the polling service delivers replies via the
                // DI-resolved sink (this adapter's SendActivitiesAsync), so a reply survives a process restart.
                await _relayProcessor.PostActivityAsync(activity, ChannelType.Instagram).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Outbound sink entry point used by the polling service. Wraps the Send in retry/backoff so a transient
        /// IG blip (429/5xx) is ridden out, while a terminal failure (dead token = 4xx) throws so the poller leaves
        /// the watermark un-advanced and logs loudly — the reply is retried next tick rather than silently dropped.
        /// </summary>
        public async Task SendActivitiesAsync(IList<Activity> activities, CancellationToken cancellationToken)
        {
            if (activities == null || activities.Count == 0)
            {
                throw new ArgumentNullException(nameof(activities));
            }

            var retry = new RetryExecutor(new RetryOptions(), _logger);

            // Retry EACH activity independently. Retrying the whole batch would re-POST an already-delivered
            // earlier message when a LATER one hits a transient 429/5xx (burst sends are exactly what provokes 429)
            // — a deterministic duplicate that the cross-tick dedup guard cannot catch because it happens before
            // any watermark/LastDeliveredActivityId is written.
            foreach (var activity in activities)
            {
                var single = new[] { activity };
                await retry.ExecuteAsync(
                    _ => ProcessOutboundActivitiesAsync(single),
                    InstagramFaultClassifier.IsTransientSendFault,
                    "Instagram.Send",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Convert outbound activities to Send API requests and deliver them to Instagram.
        /// </summary>
        public async Task ProcessOutboundActivitiesAsync(IList<Activity> outboundActivities)
        {
            if (outboundActivities == null || outboundActivities.Count == 0)
            {
                throw new ArgumentNullException(nameof(outboundActivities));
            }

            // The poller sets ReplyToId to the inbound user's IGSID (see ConversationPollingService.SendReplyActivity).
            var recipientId = outboundActivities[0]?.ReplyToId;
            var sendRequests = InstagramHelper.ActivityToInstagram(outboundActivities, recipientId, _useHumanAgentTag);

            if (sendRequests.Count > 0)
            {
                await _instagramClient.SendMessagesAsync(sendRequests).ConfigureAwait(false);
                _logger?.LogInformation("Instagram outbound delivery succeeded ({Count} activity/ies).", outboundActivities.Count);
            }
        }

        /// <summary>
        /// Read the raw request body bytes (needed for HMAC signature validation). Rewinds the buffered stream.
        /// </summary>
        private static async Task<byte[]> ReadRawBodyAsync(HttpRequest request)
        {
            if (request.Body.CanSeek)
            {
                request.Body.Position = 0;
            }

            using (var memoryStream = new MemoryStream())
            {
                await request.Body.CopyToAsync(memoryStream).ConfigureAwait(false);

                if (request.Body.CanSeek)
                {
                    request.Body.Position = 0;
                }

                return memoryStream.ToArray();
            }
        }
    }
}
