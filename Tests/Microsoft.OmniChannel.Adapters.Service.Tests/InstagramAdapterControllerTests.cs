// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.Adapters.Instagram;
using Microsoft.OmniChannel.Adapters.Service.Controllers;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.OmniChannel.Adapters.Service.Tests
{
    public class InstagramAdapterControllerTests
    {
        private const string VerifyToken = "verify-token";

        private readonly Mock<IAdapterBuilder> _mockAdapter = new Mock<IAdapterBuilder>();

        private InstagramAdapterController BuildController(string body)
        {
            var logger = new Mock<ILogger<InstagramAdapterController>>();
            var resolver = new Mock<AdapterServiceResolver>();
            resolver.Setup(r => r.Invoke(ChannelType.Instagram)).Returns(_mockAdapter.Object);

            var options = Options.Create(new InstagramAdapterConfiguration { VerifyToken = VerifyToken });

            var controller = new InstagramAdapterController(logger.Object, resolver.Object, options);

            var httpContext = new DefaultHttpContext();
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                httpContext.Request.Body = new MemoryStream(bytes);
                httpContext.Request.ContentLength = bytes.Length;
            }

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        [Fact]
        public void VerifyReturnsChallengeWhenTokenMatches()
        {
            var controller = BuildController(body: null);

            var result = controller.Verify("subscribe", VerifyToken, "challenge-123");

            var content = Assert.IsType<ContentResult>(result);
            Assert.Equal("challenge-123", content.Content);
        }

        [Fact]
        public void VerifyReturnsForbiddenWhenTokenWrong()
        {
            var controller = BuildController(body: null);

            var result = controller.Verify("subscribe", "wrong-token", "challenge-123");

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.Forbidden, objectResult.StatusCode);
        }

        [Fact]
        public async Task PostActivityAsyncReturnsOkOnSuccess()
        {
            _mockAdapter
                .Setup(a => a.ProcessInboundActivitiesAsync(It.IsAny<JToken>(), It.IsAny<HttpRequest>()))
                .Returns(Task.CompletedTask);

            var controller = BuildController("{\"object\":\"instagram\"}");

            var result = await controller.PostActivityAsync();

            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal((int)HttpStatusCode.OK, statusResult.StatusCode);
        }

        [Fact]
        public async Task PostActivityAsyncReturnsBadRequestOnEmptyBody()
        {
            var controller = BuildController(string.Empty);

            var result = await controller.PostActivityAsync();

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task PostActivityAsyncReturnsForbiddenOnSignatureFailure()
        {
            _mockAdapter
                .Setup(a => a.ProcessInboundActivitiesAsync(It.IsAny<JToken>(), It.IsAny<HttpRequest>()))
                .Returns(Task.FromException(new InvalidOperationException("Signature validation failed.")));

            var controller = BuildController("{\"object\":\"instagram\"}");

            var result = await controller.PostActivityAsync();

            var objectResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal((int)HttpStatusCode.Forbidden, objectResult.StatusCode);
        }
    }
}
