// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using System.Collections.Generic;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// A small relay-owned DTO for the result of a Direct Line activity poll, so callers never touch the SDK's
    /// <c>ActivitySet</c> type directly (keeps the seam testable and the dependency one-directional).
    /// </summary>
    public sealed class DirectLineActivitySet
    {
        public DirectLineActivitySet(IList<Activity> activities, string watermark)
        {
            Activities = activities ?? new List<Activity>();
            Watermark = watermark;
        }

        /// <summary>The activities returned for this poll (empty, never null).</summary>
        public IList<Activity> Activities { get; }

        /// <summary>The watermark to pass on the next poll to avoid re-reading these activities.</summary>
        public string Watermark { get; }
    }
}
