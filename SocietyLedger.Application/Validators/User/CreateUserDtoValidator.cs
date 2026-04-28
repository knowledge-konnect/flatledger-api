using FluentValidation;
using SocietyLedger.Application.DTOs.User;
using SocietyLedger.Domain.Constants;

namespace SocietyLedger.Application.Validators.User
{
    public class CreateUserDtoValidator : AbstractValidator<CreateUserDto>
    {
        public CreateUserDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("User name is required.")
                .MaximumLength(100)
                .WithMessage("User name cannot exceed 100 characters.");

            RuleFor(x => x.Email)
                .EmailAddress()
                .WithMessage("Email must be a valid email address.")
                .MaximumLength(255)
                .WithMessage("Email cannot exceed 255 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.Email));

            RuleFor(x => x.Mobile)
                .Matches(@"^[0-9]{10}$")
                .When(x => !string.IsNullOrWhiteSpace(x.Mobile))
                .WithMessage("Mobile must be a valid 10-digit number.");

            RuleFor(x => x.Username)
                .MaximumLength(50)
                .WithMessage("Username cannot exceed 50 characters.")
                .Matches("^[a-zA-Z0-9_.-]+$")
                .WithMessage("Username can only contain letters, numbers, underscores, dots, and hyphens.")
                .When(x => !string.IsNullOrWhiteSpace(x.Username));

            RuleFor(x => x)
                .Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Username))
                .WithMessage("Either email or username is required.");

            // RoleCode is optional; when provided it must be one of the two supported roles.
            RuleFor(x => x.RoleCode)
                .Must(code => string.IsNullOrWhiteSpace(code)
                              || code == RoleCodes.SocietyAdmin
                              || code == RoleCodes.Viewer)
                .WithMessage($"Role code must be '{RoleCodes.SocietyAdmin}' or '{RoleCodes.Viewer}'.");

            RuleFor(x => x.Password)
                .NotEmpty()
                .WithMessage("Password is required.")
                .MinimumLength(8)
                .WithMessage("Password must be at least 8 characters.")
                .MaximumLength(128)
                .WithMessage("Password cannot exceed 128 characters.")
                .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
                .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
                .Matches("[0-9]").WithMessage("Password must contain at least one number.");
        }
    }
}
