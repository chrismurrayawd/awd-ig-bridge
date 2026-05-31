// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Bot.Connector.DirectLine;
using Microsoft.OmniChannel.MessageRelayProcessor.DirectLine;
using Microsoft.OmniChannel.MessageRelayProcessor.Resilience;
using Microsoft.OmniChannel.MessageRelayProcessor.Tests.TestDoubles;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.MessageRelayProcessor.Tests
{
    public class DirectLineRetryTests
    {
        private static RetryExecutor NoWaitExecutor(int maxAttempts = 4) =>
            new RetryExecutor(
                new RetryOptions { MaxAttempts = maxAttempts },
                logger: null,
                delay: (_, __) => Task.CompletedTask,   // no real waiting in tests
                jitter: () => 1.0);                      // deterministic backoff

        private static HttpOperationException Hoe(int status) =>
            new HttpOperationException("boom")
            {
                Response = new HttpResponseMessageWrapper(new HttpResponseMessage((HttpStatusCode)status), string.Empty),
            };

        // ---- RetryExecutor --------------------------------------------------

        [Fact]
        public async Task Execute_SucceedsFirstAttempt_NoRetry()
        {
            var attempts = 0;
            var result = await NoWaitExecutor().ExecuteAsync(
                _ => { attempts++; return Task.FromResult("ok"); },
                TransientFaultClassifier.IsTransientDirectLineFault, "op", CancellationToken.None);

            Assert.Equal("ok", result);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task Execute_TransientThenSuccess_RetriesAndRecovers()
        {
            var attempts = 0;
            var result = await NoWaitExecutor().ExecuteAsync<string>(
                _ =>
                {
                    attempts++;
                    if (attempts < 3)
                    {
                        throw new HttpRequestException("blip");
                    }

                    return Task.FromResult("recovered");
                },
                TransientFaultClassifier.IsTransientDirectLineFault, "op", CancellationToken.None);

            Assert.Equal("recovered", result);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task Execute_TerminalFault_NotRetried()
        {
            var attempts = 0;
            await Assert.ThrowsAsync<HttpOperationException>(() => NoWaitExecutor().ExecuteAsync<string>(
                _ => { attempts++; throw Hoe(401); },
                TransientFaultClassifier.IsTransientDirectLineFault, "op", CancellationToken.None));

            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task Execute_AlwaysTransient_GivesUpAfterMaxAttempts()
        {
            var attempts = 0;
            await Assert.ThrowsAsync<HttpRequestException>(() => NoWaitExecutor(maxAttempts: 4).ExecuteAsync<string>(
                _ => { attempts++; throw new HttpRequestException("down"); },
                TransientFaultClassifier.IsTransientDirectLineFault, "op", CancellationToken.None));

            Assert.Equal(4, attempts);
        }

        [Fact]
        public async Task Execute_CallerCancellation_PropagatesWithoutRetry()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var attempts = 0;

            await Assert.ThrowsAsync<OperationCanceledException>(() => NoWaitExecutor().ExecuteAsync<string>(
                _ => { attempts++; return Task.FromResult("x"); },
                TransientFaultClassifier.IsTransientDirectLineFault, "op", cts.Token));

            Assert.Equal(0, attempts);
        }

        // ---- TransientFaultClassifier ---------------------------------------

        [Theory]
        [InlineData(429, true)]
        [InlineData(500, true)]
        [InlineData(502, true)]
        [InlineData(503, true)]
        [InlineData(504, true)]
        [InlineData(400, false)]
        [InlineData(401, false)]
        [InlineData(403, false)]
        [InlineData(404, false)]
        public void Classifier_HttpStatus_TransientWhen5xxOr429(int status, bool expectedTransient)
        {
            Assert.Equal(expectedTransient, TransientFaultClassifier.IsTransientDirectLineFault(Hoe(status)));
        }

        [Fact]
        public void Classifier_NetworkAndTimeout_AreTransient_UnknownTerminal()
        {
            Assert.True(TransientFaultClassifier.IsTransientDirectLineFault(new HttpRequestException("net")));
            Assert.True(TransientFaultClassifier.IsTransientDirectLineFault(new TaskCanceledException("timeout")));
            Assert.False(TransientFaultClassifier.IsTransientDirectLineFault(new InvalidOperationException("?")));
            Assert.False(TransientFaultClassifier.IsTransientDirectLineFault(null));
        }

        // ---- ResilientDirectLineGateway -------------------------------------

        [Fact]
        public async Task ResilientGateway_GetActivities_TransientThenSuccess_Retries()
        {
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueGetFault(new HttpRequestException("blip"));
            gateway.EnqueueGetFault(new HttpRequestException("blip"));
            gateway.EnqueueActivities(new List<Activity>(), "wm9");
            var resilient = new ResilientDirectLineGateway(gateway, NoWaitExecutor());

            var set = await resilient.GetActivitiesAsync("conv-1", null, CancellationToken.None);

            Assert.Equal("wm9", set.Watermark);
            Assert.Equal(3, gateway.GetCalls);
        }

        [Fact]
        public async Task ResilientGateway_GetActivities_Terminal401_NotRetried()
        {
            var gateway = new FakeDirectLineGateway();
            gateway.EnqueueGetFault(Hoe(401));
            var resilient = new ResilientDirectLineGateway(gateway, NoWaitExecutor());

            await Assert.ThrowsAsync<HttpOperationException>(
                () => resilient.GetActivitiesAsync("conv-1", null, CancellationToken.None));

            Assert.Equal(1, gateway.GetCalls);
        }
    }
}
