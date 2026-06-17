using FluentValidation;
using SocietyLedger.Application.DTOs.ContactUs;

namespace SocietyLedger.Application.Validators.ContactUs
{
    public class ContactUsRequestValidator : AbstractValidator<ContactUsRequest>
    {
        public ContactUsRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
                .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Please enter a valid email address.")
                .MaximumLength(200).WithMessage("Email must not exceed 200 characters.");

            RuleFor(x => x.Phone)
                .Matches(@"^[6-9]\d{9}$").WithMessage("Enter a valid 10-digit mobile number.")
                .When(x => !string.IsNullOrWhiteSpace(x.Phone));

            RuleFor(x => x.Subject)
                .NotEmpty().WithMessage("Subject is required.")
                .MinimumLength(3).WithMessage("Subject must be at least 3 characters.")
                .MaximumLength(200).WithMessage("Subject must not exceed 200 characters.");

            RuleFor(x => x.Message)
                .NotEmpty().WithMessage("Message is required.")
                .MinimumLength(10).WithMessage("Message must be at least 10 characters.")
                .MaximumLength(2000).WithMessage("Message must not exceed 2000 characters.");
        }
    }
}
