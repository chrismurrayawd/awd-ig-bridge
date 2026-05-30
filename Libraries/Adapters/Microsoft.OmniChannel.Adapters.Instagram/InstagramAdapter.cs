// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.MessageRelayProcessor;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Processes inbound and outbound Instagram Direct messages.
    /// Inbound: validate the Meta signature, map the webhook payload to Bot Framework activities, hand to the relay.
    /// Outbound: convert agent reply activities into Instagram Send API calls.
    /// </summary>
    public class InstagramAdapter : IAdapterBuilder
    {
        private readonly IRelayProcessor _relayProcessor;
        private readonly InstagramClientWrapper _instagramClient;
        private readonly bool _useHumanAgentTag;
        private readonly ILogger _logger;

        /// <summary>Callback raised by the relay processor with activities to send back to Instagram.</summary>
        private event EventHandler<IList<Activity>> InstagramActivitiesReceived;

        /// <summary>
        /// Initializes a new instance of the <see cref="InstagramAdapter"/> class using configuration settings.
        /// </summary>
        public InstagramAdapter(IRelayProcessor relayProcessor, IOptions<InstagramAdapterConfiguration> instagramAdapterConfiguration, IInstagramTokenProvider tokenProvider, ILogger<InstagramAdapter> logger = null)
            : this(new InstagramClientWrapper(instagramAdapterConfiguration, tokenProvider))
        {
            _relayProcessor = relayProcessor;
            _useHumanAgentTag = instagramAdapterConfiguration?.Value?.UseHumanAgentTag ?? false;
            _logger = logger;
            InstagramActivitiesReceived += OnActivitiesReceived;
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

            if (!_instagramClient.ValidateSignature(rawBody, request))
            {
                throw new InvalidOperationException(Constant.InvalidSignatureExceptionMessage);
            }

            var payload = content.ToObject<InstagramWebhookModel>();
            var activities = InstagramHelper.PayloadToActivities(payload);

            foreach (var activity in activities)
            {
                await _relayProcessor.PostActivityAsync(activity, InstagramActivitiesReceived).ConfigureAwait(false);
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

            // The relay sets ReplyToId to the inbound user's IGSID (see RelayProcessor.SendReplyActivity).
            var recipientId = outboundActivities[0]?.ReplyToId;
            var sendRequests = InstagramHelper.ActivityToInstagram(outboundActivities, recipientId, _useHumanAgentTag);

            if (sendRequests.Count > 0)
            {
                await _instagramClient.SendMessagesAsync(sendRequests).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Relay callback. Runs on the polling thread, so failures are logged rather than thrown (async void).
        /// </summary>
        private async void OnActivitiesReceived(object sender, IList<Activity> outboundActivities)
        {
            try
            {
                if (outboundActivities == null)
                {
                    throw new ArgumentNullException(nameof(outboundActivities));
                }

                await ProcessOutboundActivitiesAsync(outboundActivities).ConfigureAwait(false);
                _logger?.LogInformation("Instagram outbound delivery succeeded ({Count} activity/ies).", outboundActivities.Count);
            }
            catch (Exception ex)
            {
                // This runs on the relay polling thread — without a real log sink this failure is invisible,
                // which is exactly how a dead token / Send-API error silently swallowed agent replies before.
                if (_logger != null)
                {
                    _logger.LogError(ex, "Instagram OUTBOUND delivery FAILED — agent reply not delivered to the customer.");
                }
                else
                {
                    System.Diagnostics.Trace.TraceError($"Instagram outbound delivery failed: {ex}");
                }
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
