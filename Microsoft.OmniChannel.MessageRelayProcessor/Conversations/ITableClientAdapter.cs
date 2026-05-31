// Copyright (c) Microsoft Corporation. All rights reserved.

using Azure.Data.Tables;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Conversations
{
    /// <summary>
    /// Thin seam over the Azure Table operations <see cref="TableConversationStore"/> needs, so the store's
    /// ConversationRow ↔ TableEntity mapping is unit-testable with a Mock without a live table — the exact twin of
    /// P1's <c>ISecretClientAdapter</c>. Translates Azure status codes to neutral signals (404 → null, 409 → false,
    /// 412/404-on-update → false) so the store never sees <c>RequestFailedException</c>.
    /// </summary>
    public interface ITableClientAdapter
    {
        /// <summary>Returns the entity, or null when it does not exist (404).</summary>
        Task<TableEntity> GetEntityAsync(string partitionKey, string rowKey, CancellationToken cancellationToken);

        /// <summary>Insert-if-absent. Returns false on a 409 conflict; on success updates the entity's ETag.</summary>
        Task<bool> TryAddEntityAsync(TableEntity entity, CancellationToken cancellationToken);

        /// <summary>Replace using the entity's ETag as If-Match. Returns false on 412/404; on success updates the ETag.</summary>
        Task<bool> UpdateEntityAsync(TableEntity entity, CancellationToken cancellationToken);

        /// <summary>Returns all entities whose Status property equals the given value.</summary>
        Task<IReadOnlyList<TableEntity>> QueryByStatusAsync(string status, CancellationToken cancellationToken);
    }
}
