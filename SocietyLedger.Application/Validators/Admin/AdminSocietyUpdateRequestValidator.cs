using FluentValidation;
using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Validators.Admin
{
    public class AdminSocietyUpdateRequestValidator : AbstractValidator<AdminSocietyUpdateRequest>
    {
        public AdminSocietyUpdateRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Society name is required.")
                .MaximumLength(200);
            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required.")
                .Length(3).WithMessage("Currency must be a 3-letter code.");
            RuleFor(x => x.DefaultMaintenanceCycle)
                .NotEmpty().WithMessage("Default maintenance cycle is required.");
        }
    }
}
