using FluentValidation;
using SocietyLedger.Application.DTOs.Admin;

namespace SocietyLedger.Application.Validators.Admin
{
    public class AdminLoginRequestValidator : AbstractValidator<AdminLoginRequest>
    {
        public AdminLoginRequestValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Invalid email address.")
                .MaximumLength(255);

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.");
                //.MinimumLength(8).WithMessage("Password must be at least 8 characters.");
        }
    }
}
