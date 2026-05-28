using FluentValidation;
using SocietyLedger.Application.DTOs.Auth;

namespace SocietyLedger.Application.Validators.Auth
{
    public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
    {
        public ForgotPasswordRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty()
                .WithMessage("Email is required.")
                .EmailAddress()
                .WithMessage("Email must be a valid email address.")
                .MaximumLength(255)
                .WithMessage("Email cannot exceed 255 characters.");

            RuleFor(x => x.Mobile)
                .NotEmpty()
                .WithMessage("Mobile number is required.")
                .Matches(@"^[0-9]{10}$")
                .WithMessage("Mobile number must be exactly 10 digits.");
        }
    }
}
