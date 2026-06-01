using FluentValidation;
using SocietyLedger.Application.DTOs.Contact;

namespace SocietyLedger.Application.Validators.Contact
{
    public class ContactUsRequestValidator : AbstractValidator<ContactUsRequest>
    {
        public ContactUsRequestValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required.")
                .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email must be a valid email address.")
                .MaximumLength(255).WithMessage("Email cannot exceed 255 characters.");

            RuleFor(x => x.Subject)
                .MaximumLength(200).WithMessage("Subject cannot exceed 200 characters.")
                .When(x => x.Subject is not null);

            RuleFor(x => x.Message)
                .NotEmpty().WithMessage("Message is required.")
                .MaximumLength(2000).WithMessage("Message cannot exceed 2000 characters.");
        }
    }
}
