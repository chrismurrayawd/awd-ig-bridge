// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Conversations
{
    /// <summary>
    /// In-memory <see cref="IConversationStore"/> for local dev / tests (no Azure), and the test fake. Models
    /// Table Storage's optimistic concurrency with a per-store incrementing version string as the ETag, so the
    /// If-Match path is genuinely exercised. Hands out and stores CLONES, so a caller mutating a returned row does
    /// not change the store until it writes back. NOT durable — state is lost on restart, which is precisely why
    /// production uses the Table-backed store. Thread-safe.
    /// </summary>
    public sealed class InMemoryConversationStore : IConversationStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, ConversationRow> _rows = new Dictionary<string, ConversationRow>(StringComparer.Ordinal);
        private long _version;

        private static string Key(string channelType, string igsid) => channelType + "|" + igsid;

        public Task<ConversationRow> GetAsync(string channelType, string igsid, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_rows.TryGetValue(Key(channelType, igsid), out var row) ? row.Clone() : null);
            }
        }

        public Task<IReadOnlyList<ConversationRow>> ListActiveAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                IReadOnlyList<ConversationRow> active = _rows.Values
                    .Where(r => r.Status == ConversationStatus.Active)
                    .Select(r => r.Clone())
                    .ToList();
                return Task.FromResult(active);
            }
        }

        public Task CreateAsync(ConversationRow row, CancellationToken cancellationToken)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            lock (_gate)
            {
                var key = Key(row.ChannelType, row.Igsid);
                if (_rows.ContainsKey(key))
                {
                    throw new ConversationAlreadyExistsException(row.ChannelType, row.Igsid);
                }

                row.ETag = NextETag();
                _rows[key] = row.Clone();
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryUpdateAsync(ConversationRow row, CancellationToken cancellationToken)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            lock (_gate)
            {
                var key = Key(row.ChannelType, row.Igsid);
                if (!_rows.TryGetValue(key, out var stored) ||
                    !string.Equals(stored.ETag, row.ETag, StringComparison.Ordinal))
                {
                    // No such row, or the caller's ETag is stale → reject (mirrors Table 404 / 412 If-Match).
                    return Task.FromResult(false);
                }

                row.ETag = NextETag();
                _rows[key] = row.Clone();
                return Task.FromResult(true);
            }
        }

        public Task<int> SweepStaleAsync(TimeSpan maxIdle, DateTimeOffset now, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var swept = 0;
                foreach (var stored in _rows.Values.Where(r => r.Status == ConversationStatus.Active).ToList())
                {
                    if (now - stored.LastInboundOrReplyOn > maxIdle)
                    {
                        stored.Status = ConversationStatus.Ended;
                        stored.ETag = NextETag();
                        swept++;
                    }
                }

                return Task.FromResult(swept);
            }
        }

        private string NextETag() => (++_version).ToString();
    }
}
