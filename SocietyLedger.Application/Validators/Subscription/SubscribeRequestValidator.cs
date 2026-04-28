using FluentValidation;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Domain.Constants;

namespace SocietyLedger.Application.Validators.Subscription
{
    public class SubscribeRequestValidator : AbstractValidator<SubscribeRequest>
    {
        public SubscribeRequestValidator()
        {
            RuleFor(x => x.PlanId)
                .NotEmpty().WithMessage("Plan ID is required.");

            RuleFor(x => x.PaymentMethod)
                .NotEmpty().WithMessage("Payment method is required.")
                // Razorpay payments must go through POST /payment/create-order → verify-payment.
                // Allowing razorpay here would create a Pending invoice that never gets marked Paid.
                .Must(method => new[]
                {
                    PaymentModeCodes.BankTransfer,
                    PaymentModeCodes.Upi,
                    PaymentModeCodes.Cash,
                    PaymentModeCodes.Cheque
                }.Contains(method.ToLower()))
                .WithMessage("Invalid payment method. Use bank_transfer, upi, cash, or cheque. For online payments use POST /payment/create-order.");
        }
    }
}