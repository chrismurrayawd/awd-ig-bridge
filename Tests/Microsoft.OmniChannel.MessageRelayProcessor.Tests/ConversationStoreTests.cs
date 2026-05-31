// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests
{
    public class ConversationStoreTests
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private static ConversationRow Row(string igsid, string channel = "Instagram", ConversationStatus status = ConversationStatus.Active) =>
            new ConversationRow
            {
                ChannelType = channel,
                Igsid = igsid,
                ConversationId = "dl-" + igsid,
                WaterMark = null,
                Status = status,
                CreatedOn = Now,
                LastPolledOn = Now,
                LastInboundOrReplyOn = Now,
            };

        [Fact]
        public async Task Create_Then_Get_RoundTrips_AndAssignsETag()
        {
            var store = new InMemoryConversationStore();
            var row = Row("igsid-1");

            await store.CreateAsync(row, CancellationToken.None);
            Assert.False(string.IsNullOrEmpty(row.ETag));

            var got = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);
            Assert.NotNull(got);
            Assert.Equal("dl-igsid-1", got.ConversationId);
            Assert.Equal(ConversationStatus.Active, got.Status);
            Assert.Equal(row.ETag, got.ETag);
        }

        [Fact]
        public async Task Get_Missing_ReturnsNull()
        {
            var store = new InMemoryConversationStore();
            Assert.Null(await store.GetAsync("Instagram", "nope", CancellationToken.None));
        }

        [Fact]
        public async Task Create_Twice_SameKey_Throws()
        {
            var store = new InMemoryConversationStore();
            await store.CreateAsync(Row("igsid-1"), CancellationToken.None);

            await Assert.ThrowsAsync<ConversationAlreadyExistsException>(
                () => store.CreateAsync(Row("igsid-1"), CancellationToken.None));
        }

        [Fact]
        public async Task ListActive_ReturnsOnlyActiveRows()
        {
            var store = new InMemoryConversationStore();
            await store.CreateAsync(Row("a"), CancellationToken.None);
            await store.CreateAsync(Row("b"), CancellationToken.None);
            var ended = Row("c");
            await store.CreateAsync(ended, CancellationToken.None);
            ended.Status = ConversationStatus.Ended;
            Assert.True(await store.TryUpdateAsync(ended, CancellationToken.None));

            var active = await store.ListActiveAsync(CancellationToken.None);

            Assert.Equal(2, active.Count);
            Assert.Contains(active, r => r.Igsid == "a");
            Assert.Contains(active, r => r.Igsid == "b");
            Assert.DoesNotContain(active, r => r.Igsid == "c");
        }

        [Fact]
        public async Task TryUpdate_WithCurrentETag_Succeeds_AndAdvancesETag()
        {
            var store = new InMemoryConversationStore();
            var row = Row("igsid-1");
            await store.CreateAsync(row, CancellationToken.None);
            var etagBefore = row.ETag;

            row.WaterMark = "wm1";
            Assert.True(await store.TryUpdateAsync(row, CancellationToken.None));
            Assert.NotEqual(etagBefore, row.ETag);

            var got = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);
            Assert.Equal("wm1", got.WaterMark);
        }

        [Fact]
        public async Task TryUpdate_WithStaleETag_ReturnsFalse_LeavingStoreUnchanged()
        {
            var store = new InMemoryConversationStore();
            await store.CreateAsync(Row("igsid-1"), CancellationToken.None);

            // Two independent readers of the same row (both hold the same ETag).
            var first = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);
            var second = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);

            first.WaterMark = "by-first";
            Assert.True(await store.TryUpdateAsync(first, CancellationToken.None));

            // Second still holds the pre-update ETag → its write must be rejected, not clobber 'first'.
            second.WaterMark = "by-second";
            Assert.False(await store.TryUpdateAsync(second, CancellationToken.None));

            var got = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);
            Assert.Equal("by-first", got.WaterMark);
        }

        [Fact]
        public async Task TryUpdate_MissingRow_ReturnsFalse()
        {
            var store = new InMemoryConversationStore();
            var orphan = Row("ghost");
            orphan.ETag = "1";
            Assert.False(await store.TryUpdateAsync(orphan, CancellationToken.None));
        }

        [Fact]
        public async Task Get_ReturnsIsolatedCopy_MutationDoesNotLeakUntilWriteBack()
        {
            var store = new InMemoryConversationStore();
            await store.CreateAsync(Row("igsid-1"), CancellationToken.None);

            var got = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);
            got.WaterMark = "local-only";

            var again = await store.GetAsync("Instagram", "igsid-1", CancellationToken.None);
            Assert.Null(again.WaterMark);
        }

        [Fact]
        public async Task SweepStale_EndsIdleRows_KeepsRecentlyActive()
        {
            var store = new InMemoryConversationStore();

            var idle = Row("idle");
            idle.LastInboundOrReplyOn = Now.AddHours(-50);
            await store.CreateAsync(idle, CancellationToken.None);

            var fresh = Row("fresh");
            fresh.LastInboundOrReplyOn = Now.AddHours(-1);
            await store.CreateAsync(fresh, CancellationToken.None);

            var swept = await store.SweepStaleAsync(TimeSpan.FromHours(48), Now, CancellationToken.None);

            Assert.Equal(1, swept);
            Assert.Equal(ConversationStatus.Ended, (await store.GetAsync("Instagram", "idle", CancellationToken.None)).Status);
            Assert.Equal(ConversationStatus.Active, (await store.GetAsync("Instagram", "fresh", CancellationToken.None)).Status);
        }

        [Fact]
        public async Task SweepStale_IgnoresLastPolledOn_OnlyIdleSignalCounts()
        {
            var store = new InMemoryConversationStore();

            // Quiet-but-live: polled recently every tick, but no real activity for a while — must NOT be swept
            // until LastInboundOrReplyOn passes maxIdle. Here it is within the window, so it stays Active.
            var quiet = Row("quiet");
            quiet.LastInboundOrReplyOn = Now.AddHours(-10);
            quiet.LastPolledOn = Now;
            await store.CreateAsync(quiet, CancellationToken.None);

            var swept = await store.SweepStaleAsync(TimeSpan.FromHours(48), Now, CancellationToken.None);

            Assert.Equal(0, swept);
            Assert.Equal(ConversationStatus.Active, (await store.GetAsync("Instagram", "quiet", CancellationToken.None)).Status);
        }

        [Fact]
        public async Task DifferentChannels_SameIgsid_Coexist()
        {
            var store = new InMemoryConversationStore();
            await store.CreateAsync(Row("shared", channel: "Instagram"), CancellationToken.None);
            await store.CreateAsync(Row("shared", channel: "Other"), CancellationToken.None);

            Assert.Equal("Instagram", (await store.GetAsync("Instagram", "shared", CancellationToken.None)).ChannelType);
            Assert.Equal("Other", (await store.GetAsync("Other", "shared", CancellationToken.None)).ChannelType);
            Assert.Equal(2, (await store.ListActiveAsync(CancellationToken.None)).Count);
        }
    }
}
