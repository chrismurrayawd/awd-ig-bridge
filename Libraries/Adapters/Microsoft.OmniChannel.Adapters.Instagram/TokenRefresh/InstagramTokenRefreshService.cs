// Copyright (c) Microsoft Corporation. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.OmniChannel.Adapters.Instagram
{
    /// <summary>
    /// Background service that keeps the Instagram-user token alive: on a fixed interval it checks the current
    /// token and, when it is due (<see cref="InstagramTokenRefreshPolicy"/>), refreshes it and persists the
    /// new value through the provider. All failures are logged (never silent) and never crash the host.
    /// </summary>
    public class InstagramTokenRefreshService : BackgroundService
    {
        private const int DefaultCheckIntervalHours = 12;

        private readonly IInstagramTokenProvider _provider;
        private readonly IInstagramTokenRefreshClient _refreshClient;
        private readonly IOptions<InstagramAdapterConfiguration> _configuration;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<InstagramTokenRefreshService> _logger;

        public InstagramTokenRefreshService(
            IInstagramTokenProvider provider,
            IInstagramTokenRefreshClient refreshClient,
            IOptions<InstagramAdapterConfiguration> configuration,
            TimeProvider timeProvider,
            ILogger<InstagramTokenRefreshService> logger)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _refreshClient = refreshClient ?? throw new ArgumentNullException(nameof(refreshClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var hours = _configuration.Value?.TokenRefreshCheckIntervalHours ?? 0;
            var interval = TimeSpan.FromHours(hours <= 0 ? DefaultCheckIntervalHours : hours);
            _logger.LogInformation("Instagram token refresh service started (check interval {Interval}).", interval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Instagram token refresh cycle failed unexpectedly.");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>One check-and-maybe-refresh pass. Public so it can be exercised directly from tests.</summary>
        public async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            var state = await _provider.GetStateAsync(cancellationToken).ConfigureAwait(false);
            if (state == null || string.IsNullOrWhiteSpace(state.Token))
            {
                _logger.LogWarning("No Instagram token available to refresh.");
                return;
            }

            var thresholdDays = _configuration.Value?.TokenRefreshThresholdDays ?? 0;
            if (!InstagramTokenRefreshPolicy.ShouldRefresh(state, _timeProvider.GetUtcNow(), thresholdDays))
            {
                _logger.LogDebug("Instagram token healthy (expires {Expiry}); no refresh needed.", state.ExpiresOn);
                return;
            }

            try
            {
                var result = await _refreshClient.RefreshAsync(state.Token, cancellationToken).ConfigureAwait(false);
                await _provider.SetAsync(new InstagramTokenState(result.Token, result.ExpiresOn), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Instagram token refreshed successfully; new expiry {Expiry}.", result.ExpiresOn);
            }
            catch (InstagramTokenTooFreshException)
            {
                _logger.LogInformation("Instagram token is <24h old; will retry on the next cycle.");
            }
            catch (InstagramTokenExpiredException ex)
            {
                _logger.LogCritical(
                    ex,
                    "Instagram token is expired/invalid and cannot be auto-refreshed — a manual re-mint is required. " +
                    "Outbound replies will fail until the token is replaced.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Instagram token refresh failed; will retry on the next cycle.");
            }
        }
    }
}
