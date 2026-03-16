using FluentValidation;
using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Validators.Admin
{
    public class FeatureFlagCreateRequestValidator : AbstractValidator<FeatureFlagCreateRequest>
    {
        public FeatureFlagCreateRequestValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty().WithMessage("Key is required.")
                .MaximumLength(100)
                .Matches("^[a-z0-9_]+$").WithMessage("Key must be lowercase letters, numbers, and underscores only.");
        }
    }

    public class FeatureFlagUpdateRequestValidator : AbstractValidator<FeatureFlagUpdateRequest>
    {
        public FeatureFlagUpdateRequestValidator()
        {
            RuleFor(x => x.Description).MaximumLength(500);
        }
    }
}
