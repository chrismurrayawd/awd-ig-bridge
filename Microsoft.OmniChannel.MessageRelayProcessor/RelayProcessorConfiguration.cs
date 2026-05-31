// Copyright (c) Microsoft Corporation. All rights reserved.

namespace Microsoft.OmniChannel.MessageRelayProcessor
{
    /// <summary>
    /// Defines values that the relay and the conversation polling service use to connect to the Direct Line bot
    /// and to persist conversation state.
    /// </summary>
    public class RelayProcessorConfiguration
    {
        /// <summary>
        /// Direct Line Secret
        /// </summary>
        public string DirectLineSecret { get; set; }

        /// <summary>
        /// Bot Handle
        /// </summary>
        public string BotHandle { get; set; }

        /// <summary>
        /// HTTP GET Polling Interval in milliseconds
        /// </summary>
        public string PollingIntervalInMilliseconds { get; set; }

        /// <summary>
        /// Table Storage service URI (e.g. https://&lt;account&gt;.table.core.windows.net/). When set, the durable
        /// Table-backed conversation store is used (via the App Service managed identity); when empty, the
        /// in-memory fallback is used so local dev / tests need no Azure.
        /// </summary>
        public string TableServiceUri { get; set; }

        /// <summary>Name of the Table holding conversation rows. Defaults to "Conversations" when unset.</summary>
        public string ConversationsTableName { get; set; }

        /// <summary>Maximum conversations polled concurrently per tick. Defaults to 8 when ≤ 0.</summary>
        public int MaxConcurrentPolls { get; set; }

        /// <summary>
        /// Max idle (no inbound/reply) before a conversation is reaped by the stale sweep. Defaults to 48h when ≤ 0,
        /// matching D365's msdyn_autocloseafterinactivity (2880 min).
        /// </summary>
        public int ConversationMaxIdleHours { get; set; }

        /// <summary>How often the stale sweep runs, in minutes. Defaults to 60 when ≤ 0.</summary>
        public int StaleSweepIntervalMinutes { get; set; }
    }
}
