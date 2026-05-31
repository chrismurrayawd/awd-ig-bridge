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
        private readonly Dictionary<string, Queue<object>> _getResultsByConversation = new Dictionary<string, Queue<object>>(StringComparer.Ordinal);

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

        // Per-conversation scripting (for multi-conversation poll tests where call order across conversations
        // is non-deterministic): results are keyed by conversationId, falling back to the shared queue.
        public void EnqueueActivitiesFor(string conversationId, IEnumerable<Activity> activities, string watermark) =>
            Bucket(conversationId).Enqueue(new DirectLineActivitySet(activities?.ToList(), watermark));

        public void EnqueueGetFaultFor(string conversationId, Exception ex) => Bucket(conversationId).Enqueue(ex);

        private Queue<object> Bucket(string conversationId)
        {
            if (!_getResultsByConversation.TryGetValue(conversationId, out var queue))
            {
                queue = new Queue<object>();
                _getResultsByConversation[conversationId] = queue;
            }

            return queue;
        }

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

            var queue = _getResultsByConversation.TryGetValue(conversationId, out var perConversation) && perConversation.Count > 0
                ? perConversation
                : _getResults;

            if (queue.Count > 0)
            {
                var next = queue.Dequeue();
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
