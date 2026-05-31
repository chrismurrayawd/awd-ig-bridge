// Copyright (c) Microsoft Corporation. All rights reserved.

using System;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Conversations
{
    /// <summary>
    /// Durable record of one in-flight (or closed) conversation. Maps 1:1 to a Table Storage entity
    /// (PartitionKey = <see cref="ChannelType"/>, RowKey = <see cref="Igsid"/>) but is a plain POCO so the
    /// relay and poller never see Azure types — mirroring how P1's <c>StoredSecret</c> hides the Key Vault SDK.
    /// </summary>
    public sealed class ConversationRow
    {
        /// <summary>Channel the conversation belongs to (Table PartitionKey); also drives outbound sink resolution.</summary>
        public string ChannelType { get; set; }

        /// <summary>The customer's Instagram-scoped id (Table RowKey); the relay/poller key for the conversation.</summary>
        public string Igsid { get; set; }

        /// <summary>The durable Direct Line conversation id — the value that survives a restart.</summary>
        public string ConversationId { get; set; }

        /// <summary>Last Direct Line watermark whose activities were successfully DELIVERED to the customer.</summary>
        public string WaterMark { get; set; }

        /// <summary>Lifecycle state. Only <see cref="ConversationStatus.Active"/> rows are polled.</summary>
        public ConversationStatus Status { get; set; }

        /// <summary>When the row was first created.</summary>
        public DateTimeOffset CreatedOn { get; set; }

        /// <summary>When the poller last polled this row (advances every tick; observability only).</summary>
        public DateTimeOffset LastPolledOn { get; set; }

        /// <summary>
        /// When a real inbound message was posted or an agent reply was relayed — NOT bumped on empty polls.
        /// This is the idle signal the stale sweep uses, so a quiet-but-live conversation is never falsely closed.
        /// </summary>
        public DateTimeOffset LastInboundOrReplyOn { get; set; }

        /// <summary>
        /// Direct Line activity id of the last reply successfully delivered to the customer. Lets the poller skip
        /// an already-delivered activity on replay, shrinking the at-least-once duplicate window toward
        /// effectively-once for the common single-activity reply.
        /// </summary>
        public string LastDeliveredActivityId { get; set; }

        /// <summary>Owning poller instance (dormant on single-instance; reserved for a future scale-out lease).</summary>
        public string OwnerLease { get; set; }

        /// <summary>Lease expiry (dormant; reserved for a future scale-out lease).</summary>
        public DateTimeOffset? LeaseExpiry { get; set; }

        /// <summary>Opaque optimistic-concurrency token; set by the store on read / create / update.</summary>
        public string ETag { get; set; }

        /// <summary>A shallow copy — used by the store to hand out isolated snapshots and by the poller before mutating.</summary>
        public ConversationRow Clone() => (ConversationRow)MemberwiseClone();
    }
}
