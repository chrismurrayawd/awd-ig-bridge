// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.OmniChannel.MessageRelayProcessor.Resilience;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.DirectLine
{
    /// <summary>
    /// Decorates an <see cref="IDirectLineGateway"/> with retry/backoff on transient Direct Line faults (5xx/429/
    /// timeout), classified by <see cref="TransientFaultClassifier"/>. Terminal faults (auth/404) are not retried;
    /// they rethrow so the relay/poller can log loudly and (for a rehydrated 404) mark the conversation Faulted.
    /// </summary>
    public sealed class ResilientDirectLineGateway : IDirectLineGateway
    {
        private readonly IDirectLineGateway _inner;
        private readonly RetryExecutor _retry;

        public ResilientDirectLineGateway(IDirectLineGateway inner, RetryExecutor retry)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _retry = retry ?? throw new ArgumentNullException(nameof(retry));
        }

        public Task<string> StartConversationAsync(CancellationToken cancellationToken) =>
            _retry.ExecuteAsync(
                c => _inner.StartConversationAsync(c),
                TransientFaultClassifier.IsTransientDirectLineFault,
                "DirectLine.StartConversation",
                cancellationToken);

        public Task PostActivityAsync(string conversationId, Activity activity, CancellationToken cancellationToken) =>
            _retry.ExecuteAsync(
                c => _inner.PostActivityAsync(conversationId, activity, c),
                TransientFaultClassifier.IsTransientDirectLineFault,
                "DirectLine.PostActivity",
                cancellationToken);

        public Task<DirectLineActivitySet> GetActivitiesAsync(string conversationId, string watermark, CancellationToken cancellationToken) =>
            _retry.ExecuteAsync(
                c => _inner.GetActivitiesAsync(conversationId, watermark, c),
                TransientFaultClassifier.IsTransientDirectLineFault,
                "DirectLine.GetActivities",
                cancellationToken);

        public void Dispose() => _inner.Dispose();
    }
}
