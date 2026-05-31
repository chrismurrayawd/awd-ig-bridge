// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles
{
    /// <summary>
    /// Scriptable in-process <see cref="IDirectLineGateway"/> for tests — no live Direct Line. GetActivities is
    /// driven by a queue whose items are either a <see cref="DirectLineActivitySet"/> (returned) or an
    /// <see cref="Exception"/> (faulted); when the queue empties it returns an empty set with the same watermark.
    /// Records call counts and posted activities so tests can assert retry counts and delivery.
    /// </summary>
    public sealed class FakeDirectLineGateway : IDirectLineGateway
    {
        private readonly Queue<object> _getResults = new Queue<object>();

        public string ConversationId { get; set; } = "conv-1";

        public Exception StartFault { get; set; }

        public Exception PostFault { get; set; }

        public int StartCalls { get; private set; }

        public int GetCalls { get; private set; }

        public int PostCalls { get; private set; }

        public List<Activity> Posted { get; } = new List<Activity>();

        public List<string> PostedConversationIds { get; } = new List<string>();

        public bool Disposed { get; private set; }

        public void EnqueueActivities(DirectLineActivitySet set) => _getResults.Enqueue(set);

        public void EnqueueActivities(IEnumerable<Activity> activities, string watermark) =>
            _getResults.Enqueue(new DirectLineActivitySet(activities?.ToList(), watermark));

        public void EnqueueGetFault(Exception ex) => _getResults.Enqueue(ex);

        public Task<string> StartConversationAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            return StartFault != null
                ? Task.FromException<string>(StartFault)
                : Task.FromResult(ConversationId);
        }

        public Task PostActivityAsync(string conversationId, Activity activity, CancellationToken cancellationToken)
        {
            PostCalls++;
            if (PostFault != null)
            {
                return Task.FromException(PostFault);
            }

            Posted.Add(activity);
            PostedConversationIds.Add(conversationId);
            return Task.CompletedTask;
        }

        public Task<DirectLineActivitySet> GetActivitiesAsync(string conversationId, string watermark, CancellationToken cancellationToken)
        {
            GetCalls++;
            if (_getResults.Count > 0)
            {
                var next = _getResults.Dequeue();
                if (next is Exception ex)
                {
                    return Task.FromException<DirectLineActivitySet>(ex);
                }

                return Task.FromResult((DirectLineActivitySet)next);
            }

            return Task.FromResult(new DirectLineActivitySet(new List<Activity>(), watermark));
        }

        public void Dispose() => Disposed = true;
    }
}
