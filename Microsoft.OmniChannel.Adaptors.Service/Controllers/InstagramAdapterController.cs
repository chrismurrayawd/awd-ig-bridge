// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OmniChannel.Adapter.Builder;
using Microsoft.OmniChannel.Adapters.Instagram;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Service.Controllers
{
    /// <summary>
    /// Webhook endpoint for the Instagram channel. Meta calls the same URL with GET (verification handshake)
    /// and POST (message events): api/InstagramAdapter/postactivityasync.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class InstagramAdapterController : Controller
    {
        private readonly ILogger<InstagramAdapterController> _logger;
        private readonly IAdapterBuilder _instagramAdapter;
        private readonly InstagramAdapterConfiguration _configuration;

        public InstagramAdapterController(
            ILogger<InstagramAdapterController> logger,
            AdapterServiceResolver adapterAccessor,
            IOptions<InstagramAdapterConfiguration> configuration)
        {
            if (adapterAccessor == null)
            {
                throw new ArgumentNullException(nameof(adapterAccessor));
            }

            _logger = logger;
            _instagramAdapter = adapterAccessor(ChannelType.Instagram);
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Meta webhook verification handshake. Echoes back hub.challenge when hub.mode is "subscribe"
        /// and hub.verify_token matches the configured token; otherwise 403.
        /// </summary>
        [HttpGet("postactivityasync")]
        public IActionResult Verify(
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.verify_token")] string verifyToken,
            [FromQuery(Name = "hub.challenge")] string challenge)
        {
            var verified = InstagramHelper.GetVerifiedChallenge(mode, verifyToken, challenge, _configuration.VerifyToken);

            if (verified == null)
            {
                _logger?.LogWarning("Instagram webhook verification failed (mode={Mode}).", mode);
                return StatusCode(403, "Verification failed.");
            }

            // Meta expects the raw challenge value echoed back with 200.
            return Content(verified, "text/plain");
        }

        /// <summary>
        /// Accept an incoming Instagram messaging webhook event.
        /// </summary>
        [HttpPost("postactivityasync")]
        public async Task<IActionResult> PostActivityAsync()
        {
            // No [FromBody] parameter on purpose: the adapter needs the exact raw bytes to validate the
            // X-Hub-Signature-256 HMAC, and model binding would consume the stream before we could read it.
            // EnableBuffering (called before any read) makes the body re-readable by the adapter.
            Request.EnableBuffering();

            string rawBody;
            Request.Body.Position = 0;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return BadRequest("Request payload is invalid.");
            }

            JToken payload;
            try
            {
                payload = JToken.Parse(rawBody);
            }
            catch (Exception)
            {
                return BadRequest("Request payload is invalid.");
            }

            try
            {
                await _instagramAdapter.ProcessInboundActivitiesAsync(payload, Request).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // Raised on signature validation failure — reject the (possibly forged) request.
                _logger.LogWarning($"postactivityasync rejected: {ex.Message}");
                return StatusCode(403, "Signature validation failed.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"postactivityasync: {ex}");
                return StatusCode(500, "An error occured while handling your request.");
            }

            return StatusCode(200);
        }
    }
}
