// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.MessageRelayProcessor.Conversations;
using Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles;
using Moq;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests
{
    public class RelayProcessorTests
    {
        private const string Channel = "Instagram";
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private static IOptions<RelayProcessorConfiguration> Config() =>
            Options.Create(new RelayProcessorConfiguration
            {
                DirectLineSecret = "secret",
                BotHandle = "awd-instagram-bot",
                PollingIntervalInMilliseconds = "2000",
            });

        private static Activity InboundFrom(string igsid) =>
            new Activity
            {
                Type = ActivityTypes.Message,
                Text = "hi",
                From = new ChannelAccount(igsid),
                ChannelData = new { channelType = Channel },
            };

        private static RelayProcessor BuildRelay(IConversationStore store, FakeDirectLineGateway gateway) =>
            new RelayProcessor(store, gateway, Config(), new FixedTimeProvider(Now), new RecordingLogger<RelayProcessor>());

        // ---- Validation (carried over from the original tests, new signature) ----

        [Fact]
        public async Task PostActivity_NullActivity_Throws()
        {
            var relay = BuildRelay(new InMemoryConversationStore(), new FakeDirectLineGateway());
            await Assert.ThrowsAsync<ArgumentNullException>(() => relay.PostActivityAsync(null, Channel));
        }

        [Fact]
        public async Task PostActivity_InvalidActivity_ThrowsValidation()
        {
            var relay = BuildRelay(new InMemoryConversationStore(), new FakeDirectLineGateway());
            // Missing From/Type/ChannelData → fails RelayProcessorHelper.Validate.
            await Assert.ThrowsAsync<ValidationException>(() => relay.PostActivityAsync(new Activity(), Channel));
        }

        [Fact]
        public async Task PostActivity_MissingChannelType_Throws()
        {
            var relay = BuildRelay(new InMemoryConversationStore(), new FakeDirectLineGateway());
            await Assert.ThrowsAsync<ArgumentException>(() => relay.PostActivityAsync(InboundFrom("igsid-1"), null));
        }

        // ---- Ensure-or-create -----------------------------------------------

        [Fact]
        public async Task PostActivity_NewSender_StartsConversation_CreatesRow_PostsInbound()
        {
            var store = new InMemoryConversationStore();
            var gateway = new FakeDirectLineGateway { ConversationId = "dl-new" };
            var relay = BuildRelay(store, gateway);

            await relay.PostActivityAsync(InboundFrom("igsid-1"), Channel);

            Assert.Equal(1, gateway.StartCalls);
            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.NotNull(row);
            Assert.Equal(ConversationStatus.Active, row.Status);
            Assert.Equal("dl-new", row.ConversationId);
            Assert.Equal("dl-new", Assert.Single(gateway.PostedConversationIds));
        }

        [Fact]
        public async Task PostActivity_ExistingActiveRow_ReusesConversation_NoNewStart()
        {
            var store = new InMemoryConversationStore();
            await store.CreateAsync(new ConversationRow
            {
                ChannelType = Channel,
                Igsid = "igsid-1",
                ConversationId = "dl-existing",
                Status = ConversationStatus.Active,
                CreatedOn = Now,
                LastPolledOn = Now,
                LastInboundOrReplyOn = Now,
            }, CancellationToken.None);
            var gateway = new FakeDirectLineGateway { ConversationId = "dl-should-not-be-used" };
            var relay = BuildRelay(store, gateway);

            await relay.PostActivityAsync(InboundFrom("igsid-1"), Channel);

            Assert.Equal(0, gateway.StartCalls);
            Assert.Equal("dl-existing", Assert.Single(gateway.PostedConversationIds));
        }

        [Fact]
        public async Task PostActivity_EndedRow_ReturningCustomer_ReactivatesWithNewConversation()
        {
            var store = new InMemoryConversationStore();
            var ended = new ConversationRow
            {
                ChannelType = Channel,
                Igsid = "igsid-1",
                ConversationId = "dl-old",
                WaterMark = "old-wm",
                Status = ConversationStatus.Active,
                CreatedOn = Now,
                LastPolledOn = Now,
                LastInboundOrReplyOn = Now,
            };
            await store.CreateAsync(ended, CancellationToken.None);
            ended.Status = ConversationStatus.Ended;
            await store.TryUpdateAsync(ended, CancellationToken.None);

            var gateway = new FakeDirectLineGateway { ConversationId = "dl-fresh" };
            var relay = BuildRelay(store, gateway);

            await relay.PostActivityAsync(InboundFrom("igsid-1"), Channel);

            Assert.Equal(1, gateway.StartCalls);
            var row = await store.GetAsync(Channel, "igsid-1", CancellationToken.None);
            Assert.Equal(ConversationStatus.Active, row.Status);
            Assert.Equal("dl-fresh", row.ConversationId);
            Assert.Null(row.WaterMark); // reset for the new conversation
            Assert.Equal("dl-fresh", Assert.Single(gateway.PostedConversationIds));
        }

        [Fact]
        public async Task PostActivity_CreateRace_UsesWinningConversation()
        {
            // Simulate the race: Get returns null first (no row), CreateAsync throws 409 (a concurrent inbound won),
            // then the re-read returns the winner's Active row. The inbound must post to the WINNER's conversation.
            var winner = new ConversationRow
            {
                ChannelType = Channel,
                Igsid = "igsid-1",
                ConversationId = "dl-winner",
                Status = ConversationStatus.Active,
                CreatedOn = Now,
                LastPolledOn = Now,
                LastInboundOrReplyOn = Now,
                ETag = "1",
            };
            var store = new Mock<IConversationStore>();
            store.SetupSequence(s => s.GetAsync(Channel, "igsid-1", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((ConversationRow)null)   // EnsureConversation: first look
                 .ReturnsAsync(winner)                  // after 409: re-read the winner
                 .ReturnsAsync(winner);                 // BumpLastActivity re-read
            store.Setup(s => s.CreateAsync(It.IsAny<ConversationRow>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new ConversationAlreadyExistsException(Channel, "igsid-1"));
            store.Setup(s => s.TryUpdateAsync(It.IsAny<ConversationRow>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(true);

            var gateway = new FakeDirectLineGateway { ConversationId = "dl-orphan" };
            var relay = new RelayProcessor(store.Object, gateway, Config(), new FixedTimeProvider(Now), new RecordingLogger<RelayProcessor>());

            await relay.PostActivityAsync(InboundFrom("igsid-1"), Channel);

            Assert.Equal("dl-winner", Assert.Single(gateway.PostedConversationIds));
        }

        [Fact]
        public void Ctor_MissingBotHandle_Throws()
        {
            var config = Options.Create(new RelayProcessorConfiguration { DirectLineSecret = "s", PollingIntervalInMilliseconds = "2000" });
            Assert.Throws<MissingFieldException>(() =>
                new RelayProcessor(new InMemoryConversationStore(), new FakeDirectLineGateway(), config, new FixedTimeProvider(Now), new RecordingLogger<RelayProcessor>()));
        }
    }
}
