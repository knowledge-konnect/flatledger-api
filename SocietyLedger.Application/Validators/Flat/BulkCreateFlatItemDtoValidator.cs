using FluentValidation;
using SocietyLedger.Application.DTOs.Flat;

namespace SocietyLedger.Application.Validators.Flat
{
    public class BulkCreateFlatItemDtoValidator : AbstractValidator<BulkCreateFlatItemDto>
    {
        public BulkCreateFlatItemDtoValidator()
        {
            RuleFor(x => x.FlatNo)
                .NotEmpty().WithMessage("Flat number is required.")
                .MaximumLength(50).WithMessage("Flat number cannot exceed 50 characters.");

            RuleFor(x => x.OwnerName)
                .NotEmpty().WithMessage("Owner name is required.")
                .MaximumLength(100).WithMessage("Owner name cannot exceed 100 characters.");

            RuleFor(x => x.ContactMobile)
                .NotEmpty().WithMessage("Contact mobile number is required.")
                .Matches(@"^[0-9]{10}$").WithMessage("Contact mobile must be a valid 10-digit number.");

            RuleFor(x => x.ContactEmail)
                .EmailAddress().WithMessage("Contact email must be a valid email address.")
                .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));

            RuleFor(x => x.StatusCode)
                .MaximumLength(20)
                .When(x => !string.IsNullOrWhiteSpace(x.StatusCode))
                .WithMessage("Status code cannot exceed 20 characters.");
        }
    }
}
