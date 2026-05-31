// Copyright (c) Microsoft Corporation. All rights reserved.

using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Conversations
{
    /// <summary>
    /// Real <see cref="ITableClientAdapter"/> over an Azure <see cref="TableClient"/> (authenticated by the App
    /// Service managed identity / DefaultAzureCredential in Program.cs). Maps Azure status codes to the seam's
    /// neutral signals and keeps the local entity's ETag in step with the service.
    /// </summary>
    public sealed class TableClientAdapter : ITableClientAdapter
    {
        private readonly TableClient _table;

        public TableClientAdapter(TableClient table)
        {
            _table = table ?? throw new ArgumentNullException(nameof(table));
        }

        public async Task<TableEntity> GetEntityAsync(string partitionKey, string rowKey, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: cancellationToken).ConfigureAwait(false);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<bool> TryAddEntityAsync(TableEntity entity, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _table.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
                if (response.Headers.ETag.HasValue)
                {
                    entity.ETag = response.Headers.ETag.Value;
                }

                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 409)
            {
                return false;
            }
        }

        public async Task<bool> UpdateEntityAsync(TableEntity entity, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
                if (response.Headers.ETag.HasValue)
                {
                    entity.ETag = response.Headers.ETag.Value;
                }

                return true;
            }
            catch (RequestFailedException ex) when (ex.Status == 412 || ex.Status == 404)
            {
                // 412 = a concurrent writer advanced the ETag; 404 = the row was removed. Either way, caller re-reads.
                return false;
            }
        }

        public async Task<IReadOnlyList<TableEntity>> QueryByStatusAsync(string status, CancellationToken cancellationToken)
        {
            var results = new List<TableEntity>();
            var filter = TableClient.CreateQueryFilter($"Status eq {status}");
            await foreach (var entity in _table.QueryAsync<TableEntity>(filter: filter, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                results.Add(entity);
            }

            return results;
        }
    }
}
