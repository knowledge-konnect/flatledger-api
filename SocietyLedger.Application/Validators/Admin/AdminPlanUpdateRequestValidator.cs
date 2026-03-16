using FluentValidation;
using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Validators.Admin
{
    public class AdminPlanUpdateRequestValidator : AbstractValidator<AdminPlanUpdateRequest>
    {
        public AdminPlanUpdateRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Plan name is required.")
                .MaximumLength(100);
            RuleFor(x => x.MonthlyAmount)
                .GreaterThanOrEqualTo(0).WithMessage("Monthly amount must be non-negative.");
            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required.")
                .Length(3).WithMessage("Currency must be a 3-letter code.");
            RuleFor(x => x.DurationMonths)
                .GreaterThan(0).WithMessage("Duration must be at least 1 month.");
        }
    }
}