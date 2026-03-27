using FluentValidation;
using SocietyLedger.Application.DTOs.OpeningBalance;

namespace SocietyLedger.Application.Validators.OpeningBalance
{
    /// <summary>
    /// Shape-level validation for <see cref="OpeningBalanceRequest"/>.
    ///
    /// Cross-entity rule (transaction_date >= society.onboarding_date) is enforced
    /// in <c>OpeningBalanceService</c> because it requires a database round-trip to
    /// load the society aggregate.
    /// </summary>
    public class OpeningBalanceRequestValidator : AbstractValidator<OpeningBalanceRequest>
    {
        public OpeningBalanceRequestValidator()
        {
            RuleFor(x => x.TransactionDate)
                .NotEmpty()
                    .WithMessage("A transaction date is required for the opening entry.")
                .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
                    .WithMessage("Transaction date cannot be in the future.");

            RuleFor(x => x.SocietyOpeningAmount)
                .GreaterThanOrEqualTo(0)
                    .WithMessage("Society opening amount cannot be negative.");

            RuleFor(x => x.Items)
                .NotNull()
                    .WithMessage("items must be provided (use an empty list if none).");

            RuleForEach(x => x.Items)
                .ChildRules(item =>
                {
                    item.RuleFor(i => i.FlatPublicId)
                        .NotEmpty()
                            .WithMessage("Each item must have a valid FlatPublicId.");

                    item.RuleFor(i => i.Amount)
                        .GreaterThanOrEqualTo(0)
                            .WithMessage("Opening amount cannot be negative.");
                });
        }
    }
}
