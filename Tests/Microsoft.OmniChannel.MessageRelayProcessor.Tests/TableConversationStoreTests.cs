// Copyright (c) Microsoft Corporation. All rights reserved.

using Azure;
using Azure.Data.Tables;
using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests
{
    public class TableConversationStoreTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private static TableEntity Entity(string igsid, string status = "Active", string watermark = null, string lastDelivered = null, DateTimeOffset? lastActivity = null, string etag = "e0")
        {
            var entity = new TableEntity("Instagram", igsid)
            {
                ["ConversationId"] = "conv-" + igsid,
                ["WaterMark"] = watermark,
                ["Status"] = status,
                ["CreatedOn"] = Now,
                ["LastPolledOn"] = Now,
                ["LastInboundOrReplyOn"] = lastActivity ?? Now,
                ["LastDeliveredActivityId"] = lastDelivered,
            };
            entity.ETag = new ETag(etag);
            return entity;
        }

        private static ConversationRow Row(string igsid) =>
            new ConversationRow
            {
                ChannelType = "Instagram",
                Igsid = igsid,
                ConversationId = "conv-" + igsid,
                Status = ConversationStatus.Active,
                WaterMark = "wm0",
                CreatedOn = Now,
                LastPolledOn = Now,
                LastInboundOrReplyOn = Now,
            };

        [Fact]
        public async Task Get_MapsEntityToRow()
        {
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.GetEntityAsync("Instagram", "igsid-1", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Entity("igsid-1", status: "Ended", watermark: "wm5", lastDelivered: "a9", etag: "e1"));
            var store = new TableConversationStore(adapter.Object);

            var row = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);

            Assert.Equal("conv-igsid-1", row.ConversationId);
            Assert.Equal("wm5", row.WaterMark);
            Assert.Equal(ConversationStatus.Ended, row.Status);
            Assert.Equal("a9", row.LastDeliveredActivityId);
            Assert.Equal("e1", row.ETag);
        }

        [Fact]
        public async Task Get_ReturnsNull_WhenEntityMissing()
        {
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.GetEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((TableEntity)null);
            var store = new TableConversationStore(adapter.Object);

            Assert.Null(await store.GetAsync("Instagram", "missing", CancellationToken.None));
        }

        [Fact]
        public async Task Create_MapsRowToEntity_AndStoresReturnedETag()
        {
            TableEntity captured = null;
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.TryAddEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
                   .Callback<TableEntity, CancellationToken>((e, _) => { captured = e; e.ETag = new ETag("new-etag"); })
                   .ReturnsAsync(true);
            var store = new TableConversationStore(adapter.Object);
            var row = Row("igsid-1");

            await store.CreateAsync(row, CancellationToken.None);

            Assert.Equal("Instagram", captured.PartitionKey);
            Assert.Equal("igsid-1", captured.RowKey);
            Assert.Equal("Active", captured.GetString("Status"));
            Assert.Equal("conv-igsid-1", captured.GetString("ConversationId"));
            Assert.Equal("new-etag", row.ETag);
        }

        [Fact]
        public async Task Create_Conflict_ThrowsConversationAlreadyExists()
        {
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.TryAddEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);   // 409 mapped to false
            var store = new TableConversationStore(adapter.Object);

            await Assert.ThrowsAsync<ConversationAlreadyExistsException>(() => store.CreateAsync(Row("igsid-1"), CancellationToken.None));
        }

        [Fact]
        public async Task TryUpdate_ReturnsAdapterResult_AndRefreshesETagOnSuccess()
        {
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
                   .Callback<TableEntity, CancellationToken>((e, _) => e.ETag = new ETag("etag-2"))
                   .ReturnsAsync(true);
            var store = new TableConversationStore(adapter.Object);
            var row = Row("igsid-1");
            row.ETag = "etag-1";
            row.WaterMark = "wm-next";

            Assert.True(await store.TryUpdateAsync(row, CancellationToken.None));
            Assert.Equal("etag-2", row.ETag);
        }

        [Fact]
        public async Task TryUpdate_ReturnsFalse_OnStaleETag()
        {
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);   // 412 mapped to false
            var store = new TableConversationStore(adapter.Object);
            var row = Row("igsid-1");
            row.ETag = "stale";

            Assert.False(await store.TryUpdateAsync(row, CancellationToken.None));
        }

        [Fact]
        public async Task ListActive_QueriesActive_MapsRows()
        {
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.QueryByStatusAsync("Active", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<TableEntity> { Entity("a"), Entity("b") });
            var store = new TableConversationStore(adapter.Object);

            var rows = await store.ListActiveAsync(CancellationToken.None);

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.Igsid == "a");
            Assert.Contains(rows, r => r.Igsid == "b");
        }

        [Fact]
        public async Task SweepStale_EndsIdleEntities_KeepsFresh()
        {
            var adapter = new Mock<ITableClientAdapter>();
            adapter.Setup(a => a.QueryByStatusAsync("Active", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<TableEntity>
                   {
                       Entity("idle", lastActivity: Now.AddHours(-50)),
                       Entity("fresh", lastActivity: Now.AddHours(-1)),
                   });
            adapter.Setup(a => a.UpdateEntityAsync(It.IsAny<TableEntity>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var store = new TableConversationStore(adapter.Object);

            var swept = await store.SweepStaleAsync(TimeSpan.FromHours(48), Now, CancellationToken.None);

            Assert.Equal(1, swept);
            adapter.Verify(a => a.UpdateEntityAsync(
                It.Is<TableEntity>(e => e.RowKey == "idle" && e.GetString("Status") == "Ended"), It.IsAny<CancellationToken>()), Times.Once);
            adapter.Verify(a => a.UpdateEntityAsync(
                It.Is<TableEntity>(e => e.RowKey == "fresh"), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
