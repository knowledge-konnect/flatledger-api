using FluentValidation;
using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Validators.Admin
{
    public class PlatformSettingUpsertRequestValidator : AbstractValidator<PlatformSettingUpsertRequest>
    {
        public PlatformSettingUpsertRequestValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty().WithMessage("Key is required.")
                .MaximumLength(100)
                .Matches("^[a-z0-9_\\.]+$").WithMessage("Key must be lowercase letters, numbers, underscores, or dots.");
        }
    }
}
