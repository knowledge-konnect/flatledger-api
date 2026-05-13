using SocietyLedger.Application.DTOs.Razorpay;

namespace SocietyLedger.Application.Interfaces.Services
{
    public interface IRazorpayPaymentService
    {
        /// <summary>Creates a Razorpay order. Amount is resolved server-side from planId.</summary>
        Task<CreateOrderResponse> CreateOrderAsync(long userId, Guid planId);
        /// <summary>Verifies payment signature and activates subscription. userId is validated against the order's RecordedBy to prevent cross-user verification.</summary>
        Task<VerifyPaymentResponse> VerifyPaymentAsync(VerifyPaymentRequest request, long userId);
        /// <summary>Processes a Razorpay webhook event after verifying the HMAC-SHA256 signature.</summary>
        Task ProcessWebhookAsync(string rawBody, string signature, WebhookPayload payload);
    }
}