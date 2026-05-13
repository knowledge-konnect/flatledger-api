using FluentValidation;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Domain.Constants;

namespace SocietyLedger.Application.Validators.Subscription
{
    public class SubscribeRequestValidator : AbstractValidator<SubscribeRequest>
    {
        private static readonly string[] AllowedMethods =
        {
            PaymentModeCodes.Razorpay,
            PaymentModeCodes.BankTransfer,
            PaymentModeCodes.Upi,
            PaymentModeCodes.Cash,
            PaymentModeCodes.Cheque,
        };

        public SubscribeRequestValidator()
        {
            RuleFor(x => x.PlanId)
                .NotEmpty().WithMessage("Plan ID is required.");

            // PaymentMethod is optional — defaults to "razorpay" when omitted.
            // When provided it must be one of the known codes.
            RuleFor(x => x.PaymentMethod)
                .Must(method => string.IsNullOrEmpty(method) ||
                                AllowedMethods.Contains(method.ToLower()))
                .WithMessage($"Invalid payment method. Allowed values: {string.Join(", ", AllowedMethods)}.");
        }
    }
}