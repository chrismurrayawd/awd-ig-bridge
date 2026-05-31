// Copyright (c) Microsoft Corporation. All rights reserved.

namespace Microsoft.OmniChannel.MessageRelayProcessor.Conversations
{
    /// <summary>
    /// Lifecycle state of a conversation row. Only <see cref="Active"/> rows are polled.
    /// </summary>
    public enum ConversationStatus
    {
        /// <summary>In flight — the poller polls it for agent replies.</summary>
        Active = 0,

        /// <summary>Closed normally (EndOfConversation) or reaped by the stale sweep. No longer polled.</summary>
        Ended = 1,

        /// <summary>
        /// Unrecoverable — e.g. Direct Line returned 404 for the stored ConversationId after a restart (the
        /// conversation expired server-side). Surfaced loudly via logging so a dropped reply is visible, never silent.
        /// </summary>
        Faulted = 2,
    }
}
