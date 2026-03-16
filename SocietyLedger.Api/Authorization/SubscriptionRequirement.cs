using Microsoft.AspNetCore.Authorization;

namespace SocietyLedger.Api.Authorization
{
    /// <summary>
    /// Authorization requirement that enforces an active subscription or a valid trial.
    /// Evaluated by <see cref="SubscriptionAuthorizationHandler"/>.
    /// </summary>
    public class SubscriptionRequirement : IAuthorizationRequirement
    {
    }
}