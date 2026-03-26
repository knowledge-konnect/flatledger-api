using FluentValidation;
using SocietyLedger.Application.DTOs.Billing;

namespace SocietyLedger.Application.Validators.Billing
{
    public class GenerateBillForFlatRequestValidator : AbstractValidator<GenerateBillForFlatRequest>
    {
        public GenerateBillForFlatRequestValidator()
        {
            RuleFor(x => x.FlatPublicId)
                .NotEmpty().WithMessage("FlatPublicId is required.")
                .Must(id => id != Guid.Empty).WithMessage("FlatPublicId must be a valid non-empty GUID.");
        }
    }
}
