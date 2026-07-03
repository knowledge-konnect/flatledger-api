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
                .MaximumLength(100)
                .When(x => x.Name != null);

            RuleFor(x => x.Price)
                .GreaterThan(0).WithMessage("Price must be greater than 0.")
                .When(x => x.Price.HasValue);

            RuleFor(x => x.MonthlyAmount)
                .GreaterThanOrEqualTo(0).WithMessage("Monthly amount must be non-negative.")
                .When(x => x.MonthlyAmount.HasValue);

            RuleFor(x => x.MaxFlats)
                .GreaterThan(0).WithMessage("Max flats must be greater than 0.")
                .When(x => x.MaxFlats.HasValue);

            RuleFor(x => x.PlanGroup)
                .NotEmpty().WithMessage("Plan group is required.")
                .MaximumLength(100)
                .When(x => x.PlanGroup != null);

            RuleFor(x => x.DiscountPercentage)
                .InclusiveBetween(0, 100).When(x => x.DiscountPercentage.HasValue)
                .WithMessage("Discount percentage must be between 0 and 100.");

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required.")
                .Length(3).WithMessage("Currency must be a 3-letter code.")
                .When(x => x.Currency != null);

            RuleFor(x => x.DurationMonths)
                .Must(d => d == 1 || d == 12).WithMessage("DurationMonths must be either 1 or 12.")
                .When(x => x.DurationMonths.HasValue);
        }
    }
}