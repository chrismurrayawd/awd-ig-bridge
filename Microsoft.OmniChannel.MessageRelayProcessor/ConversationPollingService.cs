// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using Microsoft.Rest;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor
{
    /// <summary>
    /// The single background poller for ALL conversations. Each tick it lists the Active rows from the durable
    /// store and polls each (bounded concurrency) for agent replies, delivering them via the DI-resolved
    /// <see cref="IOutboundActivitySink"/> and persisting the watermark only AFTER delivery. Rehydration is just
    /// this loop: after a restart the same <see cref="IConversationStore.ListActiveAsync"/> returns the in-flight
    /// conversations, so a reply that arrives during/after a restart still reaches the customer — there is no
    /// separate, forgettable recovery path. Modelled on the P1 token refresher (loop, swallow-all, never crash the
    /// host, public RunOnceAsync for tests, injected TimeProvider). The shared <see cref="IDirectLineGateway"/> is
    /// bound only to the secret, so one instance polls every conversation.
    /// </summary>
    public class ConversationPollingService : BackgroundService
    {
        private const int DefaultPollingIntervalMs = 2000;
        private const int DefaultMaxConcurrentPolls = 8;
        private const int DefaultMaxIdleHours = 48;
        private const int DefaultSweepIntervalMinutes = 60;

        private readonly IConversationStore _store;
        private readonly IDirectLineGateway _gateway;
        private readonly OutboundSinkResolver _sinkResolver;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ConversationPollingService> _logger;

        private readonly string _botHandle;
        private readonly int _pollingIntervalMs;
        private readonly int _maxConcurrentPolls;
        private readonly TimeSpan _maxIdle;
        private readonly TimeSpan _sweepInterval;

        // Process-local guard: skip a conversation whose previous tick's poll has not finished, so a slow
        // conversation is never double-polled by an overlapping tick (and cannot block the rest).
        private readonly ConcurrentDictionary<string, byte> _inFlight = new ConcurrentDictionary<string, byte>();

        public ConversationPollingService(
            IConversationStore store,
            IDirectLineGateway gateway,
            OutboundSinkResolver sinkResolver,
            IOptions<RelayProcessorConfiguration> configuration,
            TimeProvider timeProvider,
            ILogger<ConversationPollingService> logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _sinkResolver = sinkResolver ?? throw new ArgumentNullException(nameof(sinkResolver));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? NullLogger<ConversationPollingService>.Instance;

            var config = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            if (string.IsNullOrWhiteSpace(config.BotHandle))
            {
                throw new MissingFieldException(nameof(RelayProcessorConfiguration.BotHandle));
            }

            _botHandle = config.BotHandle;
            _pollingIntervalMs = int.TryParse(config.PollingIntervalInMilliseconds, out var ms) && ms > 0 ? ms : DefaultPollingIntervalMs;
            _maxConcurrentPolls = config.MaxConcurrentPolls > 0 ? config.MaxConcurrentPolls : DefaultMaxConcurrentPolls;
            _maxIdle = TimeSpan.FromHours(config.ConversationMaxIdleHours > 0 ? config.ConversationMaxIdleHours : DefaultMaxIdleHours);
            _sweepInterval = TimeSpan.FromMinutes(config.StaleSweepIntervalMinutes > 0 ? config.StaleSweepIntervalMinutes : DefaultSweepIntervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Conversation polling service started (interval {IntervalMs}ms, maxConcurrent {MaxConcurrent}, maxIdle {MaxIdle}, sweep {Sweep}).",
                _pollingIntervalMs, _maxConcurrentPolls, _maxIdle, _sweepInterval);

            var interval = TimeSpan.FromMilliseconds(_pollingIntervalMs);
            var lastSweep = _timeProvider.GetUtcNow();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);

                    if (_timeProvider.GetUtcNow() - lastSweep >= _sweepInterval)
                    {
                        await RunStaleSweepOnceAsync(stoppingToken).ConfigureAwait(false);
                        lastSweep = _timeProvider.GetUtcNow();
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Conversation poll cycle failed unexpectedly.");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>One poll cycle: rehydrate the Active set and poll each with bounded concurrency. Public for tests.</summary>
        public async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            var active = await _store.ListActiveAsync(cancellationToken).ConfigureAwait(false);
            if (active.Count == 0)
            {
                return;
            }

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxConcurrentPolls,
                CancellationToken = cancellationToken,
            };

            await Parallel.ForEachAsync(active, options, PollGuardedAsync).ConfigureAwait(false);
        }

        /// <summary>Reap conversations idle past the max-idle window. Public for tests.</summary>
        public async Task RunStaleSweepOnceAsync(CancellationToken cancellationToken)
        {
            var swept = await _store.SweepStaleAsync(_maxIdle, _timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            if (swept > 0)
            {
                _logger.LogInformation("Stale sweep ended {Count} idle conversation(s) (idle > {MaxIdle}).", swept, _maxIdle);
            }
        }

        private async ValueTask PollGuardedAsync(ConversationRow row, CancellationToken cancellationToken)
        {
            var key = row.ChannelType + "|" + row.Igsid;
            if (!_inFlight.TryAdd(key, 0))
            {
                // A prior tick's poll for this conversation is still running — skip it this tick.
                return;
            }

            try
            {
                await PollConversationAsync(row, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutdown — the watermark is only advanced after delivery, so the next process re-delivers safely.
            }
            catch (Exception ex)
            {
                // One conversation's failure must never abort the batch or crash the host.
                _logger.LogError(ex, "Poll failed for conversation {Channel}/{Igsid}.", row.ChannelType, row.Igsid);
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        }

        private async Task PollConversationAsync(ConversationRow row, CancellationToken cancellationToken)
        {
            DirectLineActivitySet set;
            try
            {
                set = await _gateway.GetActivitiesAsync(row.ConversationId, row.WaterMark, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpOperationException httpOperationException) when ((int?)httpOperationException.Response?.StatusCode == 404)
            {
                // The Direct Line conversation expired server-side (e.g. across a long restart gap). A buffered
                // agent reply is unrecoverable regardless of the durable store — surface LOUDLY (never a silent
                // drop) and mark the row Faulted so it is no longer polled.
                _logger.LogCritical(
                    httpOperationException,
                    "Direct Line conversation {ConversationId} for {Channel}/{Igsid} returned 404 — it expired server-side; an agent reply may be UNRECOVERABLE. Marking the conversation Faulted.",
                    row.ConversationId, row.ChannelType, row.Igsid);
                await UpdateRowAsync(row, r => r.Status = ConversationStatus.Faulted, cancellationToken).ConfigureAwait(false);
                return;
            }

            var replies = new List<Activity>();
            var sawEndOfConversation = false;

            foreach (var activity in set.Activities)
            {
                if (activity?.From?.Id != _botHandle)
                {
                    continue; // only the bot's activities are agent traffic to relay outbound
                }

                if (activity.Type == ActivityTypes.EndOfConversation)
                {
                    sawEndOfConversation = true;
                    continue;
                }

                // Dedup guard: skip an activity we already delivered (shrinks the at-least-once duplicate window
                // toward effectively-once for the common single-activity reply, incl. watermark-inclusive re-reads).
                if (!string.IsNullOrEmpty(row.LastDeliveredActivityId) && activity.Id == row.LastDeliveredActivityId)
                {
                    continue;
                }

                replies.Add(activity);
            }

            if (replies.Count > 0)
            {
                foreach (var reply in replies)
                {
                    reply.ReplyToId = row.Igsid;            // the adapter reads this as the recipient IGSID
                    reply.ChannelId = row.ConversationId;
                }

                // DELIVER FIRST. A terminal sink failure throws here → caught by PollGuardedAsync → the watermark is
                // NOT advanced, so the reply is retried next tick rather than silently lost.
                var sink = _sinkResolver(row.ChannelType);
                await sink.SendActivitiesAsync(replies, cancellationToken).ConfigureAwait(false);

                // Record the last delivered activity id BEFORE advancing the watermark, so a crash before the
                // watermark write still lets the dedup guard skip the re-returned activity (near-once single reply).
                var lastDeliveredId = replies[replies.Count - 1].Id;
                var deliveredAt = _timeProvider.GetUtcNow();
                row = await UpdateRowAsync(row, r =>
                {
                    r.LastDeliveredActivityId = lastDeliveredId;
                    r.LastInboundOrReplyOn = deliveredAt;
                    r.LastPolledOn = deliveredAt;
                }, cancellationToken).ConfigureAwait(false);
                if (row == null)
                {
                    return; // row ended/swept concurrently
                }
            }

            // Advance the watermark (and LastPolledOn) so the next poll does not re-read these activities.
            var polledAt = _timeProvider.GetUtcNow();
            var newWatermark = set.Watermark;
            row = await UpdateRowAsync(row, r =>
            {
                r.WaterMark = newWatermark;
                r.LastPolledOn = polledAt;
            }, cancellationToken).ConfigureAwait(false);
            if (row == null)
            {
                return;
            }

            if (sawEndOfConversation)
            {
                _logger.LogInformation("EndOfConversation for {Channel}/{Igsid}; marking the conversation Ended.", row.ChannelType, row.Igsid);
                await UpdateRowAsync(row, r => r.Status = ConversationStatus.Ended, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Apply a mutation and persist it with ETag optimistic concurrency, re-reading and re-applying on a
        /// conflict (bounded). Returns the persisted row (with the new ETag) or null if the row no longer exists.
        /// </summary>
        private async Task<ConversationRow> UpdateRowAsync(ConversationRow row, Action<ConversationRow> mutate, CancellationToken cancellationToken)
        {
            var current = row;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                mutate(current);
                if (await _store.TryUpdateAsync(current, cancellationToken).ConfigureAwait(false))
                {
                    return current;
                }

                var fresh = await _store.GetAsync(current.ChannelType, current.Igsid, cancellationToken).ConfigureAwait(false);
                if (fresh == null)
                {
                    return null; // row gone (ended/swept by another writer) — nothing to persist
                }

                current = fresh;
            }

            _logger.LogWarning("Could not persist conversation update for {Channel}/{Igsid} after ETag retries.", row.ChannelType, row.Igsid);
            return current;
        }
    }
}
