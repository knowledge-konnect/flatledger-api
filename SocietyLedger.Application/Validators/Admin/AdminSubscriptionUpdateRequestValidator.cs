using FluentValidation;
using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Validators.Admin
{
    public class AdminSubscriptionUpdateRequestValidator : AbstractValidator<AdminSubscriptionUpdateRequest>
    {
        private static readonly string[] AllowedStatuses = { "trial", "active", "cancelled", "expired", "past_due" };

        public AdminSubscriptionUpdateRequestValidator()
        {
            RuleFor(x => x.PlanId)
                .NotEmpty().WithMessage("Plan is required.");
            RuleFor(x => x.Status)
                .NotEmpty().WithMessage("Status is required.")
                .Must(s => AllowedStatuses.Contains(s)).WithMessage($"Status must be one of: {string.Join(", ", AllowedStatuses)}.");
        }
    }
}
