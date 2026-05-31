// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Conversations
{
    /// <summary>
    /// Durable home for conversation state, keyed by (ChannelType, IGSID). Implemented by
    /// <c>TableConversationStore</c> in production and <see cref="InMemoryConversationStore"/> locally / in tests
    /// (no Azure) — mirroring P1's <c>IInstagramTokenStore</c> store/fallback split.
    /// </summary>
    public interface IConversationStore
    {
        /// <summary>Returns the row for the key, or null when none exists.</summary>
        Task<ConversationRow> GetAsync(string channelType, string igsid, CancellationToken cancellationToken);

        /// <summary>
        /// Returns every <see cref="ConversationStatus.Active"/> row. The poller calls this each tick; after a
        /// restart it is exactly the set of in-flight conversations to resume — rehydration is just this query.
        /// </summary>
        Task<IReadOnlyList<ConversationRow>> ListActiveAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Insert-if-absent. Throws <see cref="ConversationAlreadyExistsException"/> when a row already exists for
        /// the key, so a create-race between two concurrent inbound webhooks resolves to exactly one winner.
        /// </summary>
        Task CreateAsync(ConversationRow row, CancellationToken cancellationToken);

        /// <summary>
        /// ETag-guarded update. Returns <c>false</c> (does NOT throw) when the stored ETag has moved on, so the
        /// caller can re-read and re-apply only if its change is still valid (idempotent watermark advance).
        /// On success the passed row's <see cref="ConversationRow.ETag"/> is updated to the new token.
        /// </summary>
        Task<bool> TryUpdateAsync(ConversationRow row, CancellationToken cancellationToken);

        /// <summary>
        /// Marks every Active row whose <see cref="ConversationRow.LastInboundOrReplyOn"/> is older than
        /// <paramref name="maxIdle"/> as Ended (so a conversation D365 silently closed without an
        /// EndOfConversation is reaped rather than polled forever). Returns the number swept.
        /// </summary>
        Task<int> SweepStaleAsync(TimeSpan maxIdle, DateTimeOffset now, CancellationToken cancellationToken);
    }

    /// <summary>Thrown by <see cref="IConversationStore.CreateAsync"/> when a row already exists for the key.</summary>
    public sealed class ConversationAlreadyExistsException : Exception
    {
        public ConversationAlreadyExistsException(string channelType, string igsid)
            : base($"A conversation row already exists for channel '{channelType}' / IGSID '{igsid}'.")
        {
            ChannelType = channelType;
            Igsid = igsid;
        }

        public string ChannelType { get; }

        public string Igsid { get; }
    }
}
