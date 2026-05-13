using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace SocietyLedger.Api.Authorization
{
    /// <summary>
    /// Authorization handler that enforces the <see cref="SubscriptionRequirement"/>.
    /// Grants access only when the user has an active paid subscription or an unexpired trial.
    /// Paid subscriptions are cached for 5 minutes; trials use a shorter 1-minute TTL so
    /// near-expiry transitions are detected quickly without hammering the DB.
    /// </summary>
    public class SubscriptionAuthorizationHandler : AuthorizationHandler<SubscriptionRequirement>
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SubscriptionAuthorizationHandler> _logger;

        private static readonly TimeSpan CacheTtlPaid  = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CacheTtlTrial = TimeSpan.FromMinutes(1);

        public SubscriptionAuthorizationHandler(
            ISubscriptionService subscriptionService,
            IHttpContextAccessor httpContextAccessor,
            IMemoryCache cache,
            ILogger<SubscriptionAuthorizationHandler> logger)
        {
            _subscriptionService = subscriptionService;
            _httpContextAccessor = httpContextAccessor;
            _cache = cache;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            SubscriptionRequirement requirement)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                context.Fail();
                return;
            }

            var userIdClaim =
                context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            {
                context.Fail();
                return;
            }

            try
            {
                var cacheKey = $"sub_active_{userId}";
                if (!_cache.TryGetValue(cacheKey, out bool isActive))
                {
                    var status = await _subscriptionService.GetSubscriptionStatusAsync(userId);
                    var isTrial = status.Status == SubscriptionStatusCodes.Trial && status.TrialEndDate > DateTime.UtcNow;
                    isActive = status.Status == SubscriptionStatusCodes.Active || isTrial;

                    // Only cache positive results — expired subscriptions must fail immediately.
                    // Trials use a shorter TTL so expiry is detected within 1 minute.
                    if (isActive)
                    {
                        var ttl = isTrial ? CacheTtlTrial : CacheTtlPaid;
                        _cache.Set(cacheKey, true, ttl);
                    }
                }

                if (isActive)
                    context.Succeed(requirement);
                else
                    context.Fail();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscription check failed for user {UserId} — denying access", userId);
                context.Fail();
            }
        }
    }
}