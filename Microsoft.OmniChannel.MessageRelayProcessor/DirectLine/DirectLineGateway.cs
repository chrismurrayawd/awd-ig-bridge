// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Real <see cref="IDirectLineGateway"/> over the Bot.Connector.DirectLine SDK's <c>DirectLineClient</c>.
    /// One instance is bound to one Direct Line secret. Threads the cancellation token through every call (the
    /// original sample did not) and re-packages the SDK's ActivitySet into the relay-owned DTO.
    /// </summary>
    public sealed class DirectLineGateway : IDirectLineGateway
    {
        private readonly DirectLineClient _client;

        public DirectLineGateway(string directLineSecret)
        {
            if (string.IsNullOrWhiteSpace(directLineSecret))
            {
                throw new ArgumentException("Direct Line secret must be provided.", nameof(directLineSecret));
            }

            _client = new DirectLineClient(directLineSecret);
        }

        public async Task<string> StartConversationAsync(CancellationToken cancellationToken)
        {
            var conversation = await _client.Conversations.StartConversationAsync(cancellationToken).ConfigureAwait(false);
            if (conversation == null || string.IsNullOrEmpty(conversation.ConversationId))
            {
                throw new InvalidOperationException(
                    "Direct Line StartConversation returned no conversation. Verify the Direct Line secret in configuration.");
            }

            return conversation.ConversationId;
        }

        public Task PostActivityAsync(string conversationId, Activity activity, CancellationToken cancellationToken) =>
            _client.Conversations.PostActivityAsync(conversationId, activity, cancellationToken);

        public async Task<DirectLineActivitySet> GetActivitiesAsync(string conversationId, string watermark, CancellationToken cancellationToken)
        {
            var set = await _client.Conversations.GetActivitiesAsync(conversationId, watermark, cancellationToken).ConfigureAwait(false);
            return new DirectLineActivitySet(set?.Activities, set?.Watermark);
        }

        public void Dispose() => _client.Dispose();
    }
}
