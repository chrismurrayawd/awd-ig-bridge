// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using Microsoft.Bot.Connector.DirectLine;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor
{
    /// <summary>
    /// Inbound side of the relay: ensures a durable conversation row exists for the sender (starting a Direct Line
    /// conversation the first time) and posts the inbound activity to Direct Line. Stateless and durable — the old
    /// static in-memory cache, the per-conversation polling thread, and the per-call callback are gone; replies are
    /// delivered by <see cref="ConversationPollingService"/> reading the same <see cref="IConversationStore"/>.
    /// Registered as a singleton (it holds no per-request state); the shared <see cref="IDirectLineGateway"/> is
    /// bound only to the secret, so one instance serves every conversation.
    /// </summary>
    public class RelayProcessor : IRelayProcessor
    {
        private readonly IConversationStore _store;
        private readonly IDirectLineGateway _gateway;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<RelayProcessor> _logger;

        public RelayProcessor(
            IConversationStore store,
            IDirectLineGateway gateway,
            IOptions<RelayProcessorConfiguration> relayProcessorConfiguration,
            TimeProvider timeProvider,
            ILogger<RelayProcessor> logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? NullLogger<RelayProcessor>.Instance;

            if (relayProcessorConfiguration == null)
            {
                throw new ArgumentNullException(nameof(relayProcessorConfiguration));
            }

            // The Direct Line secret is consumed by the gateway factory and BotHandle by the poller; the relay
            // itself only needs the store + gateway. Keep BotHandle validated here so a misconfig still surfaces
            // at startup rather than at first reply.
            if (string.IsNullOrWhiteSpace(relayProcessorConfiguration.Value?.BotHandle))
            {
                throw new MissingFieldException(nameof(RelayProcessorConfiguration.BotHandle));
            }
        }

        public async Task PostActivityAsync(Activity inboundActivity, string channelType, CancellationToken cancellationToken = default)
        {
            if (inboundActivity == null)
            {
                throw new ArgumentNullException(nameof(inboundActivity));
            }

            if (string.IsNullOrWhiteSpace(channelType))
            {
                throw new ArgumentException("Channel type must be provided.", nameof(channelType));
            }

            var validationResult = RelayProcessorHelper.Validate(inboundActivity);
            if (!validationResult.IsValid)
            {
                throw new ValidationException(string.Join(" ; ", validationResult.BrokenRules));
            }

            var igsid = inboundActivity.From.Id;
            var row = await EnsureConversationAsync(channelType, igsid, cancellationToken).ConfigureAwait(false);

            await _gateway.PostActivityAsync(row.ConversationId, inboundActivity, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Relayed inbound activity to Direct Line conversation {ConversationId} ({Channel}/{Igsid}).", row.ConversationId, channelType, igsid);

            await BumpLastActivityAsync(channelType, igsid, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Return the Active conversation row for the sender, creating one (and a Direct Line conversation) if there
        /// is none, or reactivating a previously-closed row for a returning customer. The insert-if-absent create
        /// resolves a race between two concurrent inbound webhooks to exactly one winner.
        /// </summary>
        private async Task<ConversationRow> EnsureConversationAsync(string channelType, string igsid, CancellationToken cancellationToken)
        {
            var existing = await _store.GetAsync(channelType, igsid, cancellationToken).ConfigureAwait(false);
            if (existing != null && existing.Status == ConversationStatus.Active)
            {
                return existing;
            }

            // No active conversation — start a fresh Direct Line conversation for this sender.
            var conversationId = await _gateway.StartConversationAsync(cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();

            if (existing == null)
            {
                var fresh = NewRow(channelType, igsid, conversationId, now);
                try
                {
                    await _store.CreateAsync(fresh, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Started Direct Line conversation {ConversationId} for {Channel}/{Igsid}.", conversationId, channelType, igsid);
                    return fresh;
                }
                catch (ConversationAlreadyExistsException)
                {
                    // Create-race: a concurrent inbound won. Use the winner's conversation; our just-started Direct
                    // Line conversation becomes an orphan (it ages out server-side and via the stale sweep).
                    var winner = await _store.GetAsync(channelType, igsid, cancellationToken).ConfigureAwait(false);
                    if (winner != null && winner.Status == ConversationStatus.Active)
                    {
                        _logger.LogInformation("Conversation create-race for {Channel}/{Igsid}; using the winning conversation {ConversationId}.", channelType, igsid, winner.ConversationId);
                        return winner;
                    }

                    existing = winner; // very rare: winner ended between create and re-read — reactivate below.
                }
            }

            // Returning customer (previous row Ended/Faulted) or the rare race re-read: reactivate the row with the
            // new Direct Line conversation, ETag-guarded so a concurrent writer is never clobbered.
            if (existing != null)
            {
                var reactivated = existing.Clone();
                reactivated.ConversationId = conversationId;
                reactivated.WaterMark = null;
                reactivated.LastDeliveredActivityId = null;
                reactivated.Status = ConversationStatus.Active;
                reactivated.LastPolledOn = now;
                reactivated.LastInboundOrReplyOn = now;

                if (await _store.TryUpdateAsync(reactivated, cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogInformation("Reactivated conversation for {Channel}/{Igsid} with new Direct Line conversation {ConversationId}.", channelType, igsid, conversationId);
                    return reactivated;
                }

                var current = await _store.GetAsync(channelType, igsid, cancellationToken).ConfigureAwait(false);
                if (current != null && current.Status == ConversationStatus.Active)
                {
                    return current;
                }

                return reactivated; // best effort: inbound still posts to a live Direct Line conversation.
            }

            // Degenerate double-create-race (both losers) — return an unpersisted fresh row so the inbound still
            // reaches a live Direct Line conversation. Practically unreachable.
            return NewRow(channelType, igsid, conversationId, now);
        }

        /// <summary>
        /// Re-read and bump <see cref="ConversationRow.LastInboundOrReplyOn"/> so the stale sweep treats the
        /// conversation as live. Best-effort with one retry on an ETag race (a missed bump only risks a slightly
        /// early sweep of a conversation that then goes quiet).
        /// </summary>
        private async Task BumpLastActivityAsync(string channelType, string igsid, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var row = await _store.GetAsync(channelType, igsid, cancellationToken).ConfigureAwait(false);
                if (row == null || row.Status != ConversationStatus.Active)
                {
                    return;
                }

                row.LastInboundOrReplyOn = _timeProvider.GetUtcNow();
                if (await _store.TryUpdateAsync(row, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }

        private static ConversationRow NewRow(string channelType, string igsid, string conversationId, DateTimeOffset now) =>
            new ConversationRow
            {
                ChannelType = channelType,
                Igsid = igsid,
                ConversationId = conversationId,
                WaterMark = null,
                Status = ConversationStatus.Active,
                CreatedOn = now,
                LastPolledOn = now,
                LastInboundOrReplyOn = now,
            };
    }
}
