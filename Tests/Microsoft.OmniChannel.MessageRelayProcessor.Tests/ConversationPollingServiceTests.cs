// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles;
using Microsoft.Rest;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests
{
    public class ConversationPollingServiceTests
    {
        private const string Channel = "Instagram";
        private const string BotHandle = "awd-instagram-bot";
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private static IOptions<RelayProcessorConfiguration> Config() =>
            Options.Create(new RelayProcessorConfiguration
            {
                DirectLineSecret = "secret",
                BotHandle = BotHandle,
                PollingIntervalInMilliseconds = "2000",
                MaxConcurrentPolls = 8,
                ConversationMaxIdleHours = 48,
                StaleSweepIntervalMinutes = 60,
            });

        private static Activity Bot(string text, string id) =>
            new Activity { Type = ActivityTypes.Message, Text = text, Id = id, From = new ChannelAccount(BotHandle) };

        private static Activity User(string text) =>
            new Activity { Type = ActivityTypes.Message, Text = text, From = new ChannelAccount("igsid-1") };

        private static Activity EndOfConversation() =>
            new Activity { Type = ActivityTypes.EndOfConversation, From = new ChannelAccount(BotHandle) };

        private static HttpOperationException Hoe(int status) =>
            new HttpOperationException("boom")
            {
                Response = new HttpResponseMessageWrapper(new HttpResponseMessage((HttpStatusCode)status), string.Empty),
            };

        private static async Task SeedActiveAsync(IConversationStore store, string igsid, string conversationId, string watermark = null, string lastDelivered = null, DateTimeOffset? lastActivity = null)
        {
            await store.CreateAsync(new ConversationRow
            {
                ChannelType = Channel,
                Igsid = igsid,
                ConversationId = conversationId,
                Status = ConversationStatus.Active,
                WaterMark = watermark,
                LastDeliveredActivityId = lastDelivered,
                CreatedOn = Now,
                LastPolledOn = Now,
                LastInboundOrReplyOn = lastActivity ?? Now,
            }, CancellationToken.None);
        }

        private static ConversationPollingService BuildPoller(
            IConversationStore store,
            FakeDirectLineGateway gateway,
            IOutboundActivitySink sink,
            RecordingLogger<ConversationPollingService> logger = null,
            FixedTimeProvider clock = null)
        {
            OutboundSinkResolver resolver = _ => sink;
            return new ConversationPollingService(
                store, gateway, resolver, Config(), clock ?? new FixedTimeProvider(Now),
                logger ?? new RecordingLogger<ConversationPollingService>());
        }

        [Fact]
        public async Task RunOnce_RehydratesActiveRow_DeliversReplyToSink_NoInboundCall()
        {
            // The headline acceptance test: an Active row exists as if it survived a restart — NO inbound call, NO
            // closure. One poll cycle must deliver the agent reply to the customer (sink), addressed to the IGSID.
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-1", "conv-1", watermark: "wm0");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueActivities(new[] { Bot("how can we help?", "a1") }, "wm1");
            var sink = new RecordingSink();

            await BuildPoller(store, gateway, sink).RunOnceAsync(CancellationToken.None);

            var delivered = Assert.Single(sink.Received);
            Assert.Equal("how can we help?", delivered.Text);
            Assert.Equal("igsid-1", delivered.ReplyToId);

            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.Equal("wm1", row.WaterMark);
            Assert.Equal("a1", row.LastDeliveredActivityId);
        }

        [Fact]
        public async Task RunOnce_DeliversOnlyBotHandleActivities()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-1", "conv-1");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueActivities(new[] { User("customer text"), Bot("agent reply", "a1") }, "wm1");
            var sink = new RecordingSink();

            await BuildPoller(store, gateway, sink).RunOnceAsync(CancellationToken.None);

            var delivered = Assert.Single(sink.Received);
            Assert.Equal("agent reply", delivered.Text);
        }

        [Fact]
        public async Task RunOnce_MultiActivityReply_DeliversAllInOrder_LastDeliveredIsLast()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-1", "conv-1", watermark: "wm0");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueActivities(new[] { Bot("first", "a1"), Bot("second", "a2") }, "wm1");
            var sink = new RecordingSink();

            await BuildPoller(store, gateway, sink).RunOnceAsync(CancellationToken.None);

            Assert.Equal(2, sink.Received.Count);
            Assert.Equal("first", sink.Received[0].Text);
            Assert.Equal("second", sink.Received[1].Text);
            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.Equal("a2", row.LastDeliveredActivityId);
            Assert.Equal("wm1", row.WaterMark);
        }

        [Fact]
        public async Task RunOnce_SinkFails_WatermarkNotAdvanced_AndLoggedLoud()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-1", "conv-1", watermark: "wm0");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueActivities(new[] { Bot("reply", "a1") }, "wm1");
            var sink = new RecordingSink { ThrowOnSend = new InvalidOperationException("delivery failed") };
            var logger = new RecordingLogger<ConversationPollingService>();

            await BuildPoller(store, gateway, sink, logger).RunOnceAsync(CancellationToken.None);

            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.Equal("wm0", row.WaterMark);              // NOT advanced — reply will be retried next tick
            Assert.Null(row.LastDeliveredActivityId);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public async Task RunOnce_SkipsAlreadyDeliveredActivity()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-1", "conv-1", watermark: "wm0", lastDelivered: "a1");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueActivities(new[] { Bot("already sent", "a1"), Bot("new reply", "a2") }, "wm1");
            var sink = new RecordingSink();

            await BuildPoller(store, gateway, sink).RunOnceAsync(CancellationToken.None);

            var delivered = Assert.Single(sink.Received);
            Assert.Equal("new reply", delivered.Text);
            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.Equal("a2", row.LastDeliveredActivityId);
        }

        [Fact]
        public async Task RunOnce_EndOfConversation_DeliversTrailingThenMarksEnded()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-1", "conv-1");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueActivities(new[] { Bot("final reply", "a1"), EndOfConversation() }, "wm1");
            var sink = new RecordingSink();

            await BuildPoller(store, gateway, sink).RunOnceAsync(CancellationToken.None);

            Assert.Single(sink.Received);
            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.Equal(ConversationStatus.Ended, row.Status);
        }

        [Fact]
        public async Task RunOnce_DirectLine404_MarksFaulted_LogsCritical()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-1", "conv-1", watermark: "wm0");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueGetFault(Hoe(404));
            var logger = new RecordingLogger<ConversationPollingService>();

            await BuildPoller(store, gateway, new RecordingSink(), logger).RunOnceAsync(CancellationToken.None);

            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.Equal(ConversationStatus.Faulted, row.Status);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Critical);
        }

        [Fact]
        public async Task RunOnce_OneConversationFails_OthersStillDelivered()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "igsid-A", "conv-A");
            await SeedActiveAsync(store, "igsid-B", "conv-B");
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueGetFaultFor("conv-A", new InvalidOperationException("A failed"));
            gateway.EnqueueActivitiesFor("conv-B", new[] { Bot("B reply", "b1") }, "wmB");
            var sink = new RecordingSink();
            var logger = new RecordingLogger<ConversationPollingService>();

            await BuildPoller(store, gateway, sink, logger).RunOnceAsync(CancellationToken.None);

            var delivered = Assert.Single(sink.Received);
            Assert.Equal("B reply", delivered.Text);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error); // A's failure was logged, not fatal
        }

        [Fact]
        public async Task RunOnce_NoActiveRows_DoesNothing()
        {
            var gateway = new FakeDirectLineGateway();
            await BuildPoller(new InMemoryConversationStore(), gateway, new RecordingSink()).RunOnceAsync(CancellationToken.None);
            Assert.Equal(0, gateway.GetCalls);
        }

        [Fact]
        public async Task RunStaleSweep_EndsIdleConversations()
        {
            var store = new InMemoryConversationStore();
            await SeedActiveAsync(store, "idle", "conv-idle", lastActivity: Now.AddHours(-50));
            await SeedActiveAsync(store, "fresh", "conv-fresh", lastActivity: Now.AddHours(-1));

            await BuildPoller(store, new FakeDirectLineGateway(), new RecordingSink(), clock: new FixedTimeProvider(Now))
                .RunStaleSweepOnceAsync(CancellationToken.None);

            Assert.Equal(ConversationStatus.Ended, (await store.GetAsync(Channel, "idle", CancellationToken.None)).Status);
            Assert.Equal(ConversationStatus.Active, (await store.GetAsync(Channel, "fresh", CancellationToken.None)).Status);
        }

        [Fact]
        public void Ctor_MissingBotHandle_Throws()
        {
            var config = Options.Create(new RelayProcessorConfiguration { PollingIntervalInMilliseconds = "2000" });
            OutboundSinkResolver resolver = _ => new RecordingSink();
            Assert.Throws<MissingFieldException>(() =>
                new ConversationPollingService(new InMemoryConversationStore(), new FakeDirectLineGateway(), resolver, config, new FixedTimeProvider(Now)));
        }
    }
}
