// Copyright (c) Microsoft Corporation. All rights reserved.

using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Conversations
{
    /// <summary>
    /// Durable <see cref="IConversationStore"/> backed by Azure Table Storage, used in production. PartitionKey =
    /// ChannelType, RowKey = IGSID. All the ConversationRow ↔ TableEntity mapping lives here over the
    /// <see cref="ITableClientAdapter"/> seam, so it is unit-tested with a Mock and no live table — mirroring how
    /// P1's KeyVaultInstagramTokenStore sits over ISecretClientAdapter.
    /// </summary>
    public sealed class TableConversationStore : IConversationStore
    {
        private readonly ITableClientAdapter _table;

        public TableConversationStore(ITableClientAdapter table)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        public async Task<ConversationRow> GetAsync(string channelType, string igsid, CancellationToken cancellationToken)
        {
            var entity = await _table.GetEntityAsync(channelType, igsid, cancellationToken).ConfigureAwait(false);
            return entity == null ? null : ToRow(entity);
        }

        public async Task<IReadOnlyList<ConversationRow>> ListActiveAsync(CancellationToken cancellationToken)
        {
            var entities = await _table.QueryByStatusAsync(ConversationStatus.Active.ToString(), cancellationToken).ConfigureAwait(false);
            return entities.Select(ToRow).ToList();
        }

        public async Task CreateAsync(ConversationRow row, CancellationToken cancellationToken)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            var entity = ToEntity(row);
            if (!await _table.TryAddEntityAsync(entity, cancellationToken).ConfigureAwait(false))
            {
                throw new ConversationAlreadyExistsException(row.ChannelType, row.Igsid);
            }

            row.ETag = entity.ETag.ToString();
        }

        public async Task<bool> TryUpdateAsync(ConversationRow row, CancellationToken cancellationToken)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            var entity = ToEntity(row);
            var updated = await _table.UpdateEntityAsync(entity, cancellationToken).ConfigureAwait(false);
            if (updated)
            {
                row.ETag = entity.ETag.ToString();
            }

            return updated;
        }

        public async Task<int> SweepStaleAsync(TimeSpan maxIdle, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var entities = await _table.QueryByStatusAsync(ConversationStatus.Active.ToString(), cancellationToken).ConfigureAwait(false);
            var swept = 0;

            foreach (var entity in entities)
            {
                var lastActivity = entity.GetDateTimeOffset("LastInboundOrReplyOn") ?? default;
                if (now - lastActivity <= maxIdle)
                {
                    continue;
                }

                entity["Status"] = ConversationStatus.Ended.ToString();
                if (await _table.UpdateEntityAsync(entity, cancellationToken).ConfigureAwait(false))
                {
                    swept++;
                }

                // On an ETag conflict a live poll just updated the row — skip; a later sweep reaps it if still idle.
            }

            return swept;
        }

        private static TableEntity ToEntity(ConversationRow row)
        {
            var entity = new TableEntity(row.ChannelType, row.Igsid)
            {
                ["ConversationId"] = row.ConversationId,
                ["WaterMark"] = row.WaterMark,
                ["Status"] = row.Status.ToString(),
                ["CreatedOn"] = row.CreatedOn,
                ["LastPolledOn"] = row.LastPolledOn,
                ["LastInboundOrReplyOn"] = row.LastInboundOrReplyOn,
                ["LastDeliveredActivityId"] = row.LastDeliveredActivityId,
                ["OwnerLease"] = row.OwnerLease,
                ["LeaseExpiry"] = row.LeaseExpiry,
            };

            if (!string.IsNullOrEmpty(row.ETag))
            {
                entity.ETag = new ETag(row.ETag);
            }

            return entity;
        }

        private static ConversationRow ToRow(TableEntity entity) =>
            new ConversationRow
            {
                ChannelType = entity.PartitionKey,
                Igsid = entity.RowKey,
                ConversationId = entity.GetString("ConversationId"),
                WaterMark = entity.GetString("WaterMark"),
                Status = ParseStatus(entity.GetString("Status")),
                CreatedOn = entity.GetDateTimeOffset("CreatedOn") ?? default,
                LastPolledOn = entity.GetDateTimeOffset("LastPolledOn") ?? default,
                LastInboundOrReplyOn = entity.GetDateTimeOffset("LastInboundOrReplyOn") ?? default,
                LastDeliveredActivityId = entity.GetString("LastDeliveredActivityId"),
                OwnerLease = entity.GetString("OwnerLease"),
                LeaseExpiry = entity.GetDateTimeOffset("LeaseExpiry"),
                ETag = entity.ETag.ToString(),
            };

        private static ConversationStatus ParseStatus(string value) =>
            Enum.TryParse<ConversationStatus>(value, out var status) ? status : ConversationStatus.Active;
    }
}
