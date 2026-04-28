using FluentValidation;
using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Validators.Admin
{
    public class AdminPlanCreateRequestValidator : AbstractValidator<AdminPlanCreateRequest>
    {
        public AdminPlanCreateRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Plan name is required.")
                .MaximumLength(100);
            RuleFor(x => x.Price)
                .GreaterThanOrEqualTo(0).WithMessage("Price must be non-negative.");

            RuleFor(x => x.MaxFlats)
                .GreaterThan(0).WithMessage("Max flats must be greater than 0.");

            RuleFor(x => x.PlanGroup)
                .NotEmpty().WithMessage("Plan group is required.")
                .MaximumLength(100);

            RuleFor(x => x.DiscountPercentage)
                .InclusiveBetween(0, 100).When(x => x.DiscountPercentage.HasValue)
                .WithMessage("Discount percentage must be between 0 and 100.");

            RuleFor(x => x.Currency)
                .NotEmpty().WithMessage("Currency is required.")
                .Length(3).WithMessage("Currency must be a 3-letter code.");
            RuleFor(x => x.DurationMonths)
                .Must(d => d == 1 || d == 12).WithMessage("DurationMonths must be either 1 or 12.");
        }
    }
}