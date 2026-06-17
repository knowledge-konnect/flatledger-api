using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Razorpay.Api;
using SocietyLedger.Application.DTOs.Razorpay;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace SocietyLedger.Infrastructure.Services
{
    public class RazorpayPaymentService : IRazorpayPaymentService
    {
        private readonly IPaymentRepository _paymentRepo;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPlanService _planService;
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly IUserRepository _userRepo;
        private readonly ILogger<RazorpayPaymentService> _logger;
        private readonly string _keyId;
        private readonly string _keySecret;
        private readonly string _webhookSecret;
        private readonly ResiliencePipeline _razorpayRetry;

        // Razorpay orders expire after 15 minutes by default
        private static readonly TimeSpan OrderExpiry = TimeSpan.FromMinutes(15);

        public RazorpayPaymentService(
            IPaymentRepository paymentRepo,
            ISubscriptionService subscriptionService,
            IPlanService planService,
            IInvoiceRepository invoiceRepo,
            IUserRepository userRepo,
            ILogger<RazorpayPaymentService> logger,
            IConfiguration config)
        {
            _paymentRepo = paymentRepo;
            _subscriptionService = subscriptionService;
            _planService = planService;
            _invoiceRepo = invoiceRepo;
            _userRepo = userRepo;
            _logger = logger;
            _keyId = config["Razorpay:KeyId"] ?? throw new InvalidOperationException("Razorpay KeyId not configured");
            _keySecret = config["Razorpay:KeySecret"] ?? throw new InvalidOperationException("Razorpay KeySecret not configured");
            _webhookSecret = config["Razorpay:WebhookSecret"] ?? throw new InvalidOperationException("Razorpay WebhookSecret not configured");

            // Retry up to 3× with exponential back-off (1s → 2s → 4s) per attempt, each capped at 15 s.
            _razorpayRetry = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "Razorpay SDK transient failure (attempt {Attempt}/{Max}): {Error}",
                            args.AttemptNumber + 1, 3, args.Outcome.Exception?.Message);
                        return ValueTask.CompletedTask;
                    }
                })
                .AddTimeout(TimeSpan.FromSeconds(15))
                .Build();
        }

        /// <summary>
        /// Creates a Razorpay order for subscription payment. Amount is derived from the plan record, never from the client request.
        /// </summary>
        public async Task<CreateOrderResponse> CreateOrderAsync(long userId, Guid planId)
        {
            // Resolve authoritative price from the plan — never trust a client-supplied amount
            var plan = await _planService.GetPlanByIdAsync(planId);
            if (plan == null)
                throw new NotFoundException("Plan", planId.ToString());

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());

            var serverAmount = plan.Price > 0 ? plan.Price : plan.MonthlyAmount;
            if (serverAmount <= 0)
                throw new ValidationException($"Selected plan '{plan.Name}' has invalid amount ({serverAmount}). Please contact support.");

            // Reuse a recent pending order to avoid duplicates (skip if expired)
            var existingPending = await _paymentRepo.GetPendingSubscriptionPaymentBySocietyIdAsync(user.SocietyId);
            var pendingPlanId = ParsePlanIdFromReference(existingPending?.Reference);
            if (existingPending != null
                && pendingPlanId == planId
                && existingPending.Amount > 0
                && existingPending.CreatedAt >= DateTime.UtcNow - OrderExpiry)
            {
                _logger.LogInformation("Reusing existing pending order {OrderId} for user {UserId}", existingPending.RazorpayOrderId, userId);
                return new CreateOrderResponse
                {
                    OrderId = existingPending.RazorpayOrderId!,
                    Amount = existingPending.Amount,
                    Currency = "INR",
                    KeyId = _keyId
                };
            }

            var client = new RazorpayClient(_keyId, _keySecret);

            var options = new Dictionary<string, object>
            {
                { "amount", (int)(serverAmount * 100) },
                { "currency", "INR" },
                { "receipt", $"receipt_{userId}_{DateTime.UtcNow.Ticks}" },
                {
                    "notes", new Dictionary<string, string>
                    {
                        { "society_id", user.SocietyId.ToString() }
                    }
                }
            };

            // SDK call is synchronous — offload to avoid blocking a thread-pool thread; retried up to 3× on transient failures
            dynamic? order = null;
            await _razorpayRetry.ExecuteAsync(async ct =>
            {
                order = await Task.Run(() => client.Order.Create(options), ct);
            });
            // Explicit cast from dynamic to string — prevents dynamic dispatch errors on logger extension methods
            var razorpayOrderId = (string)order!["id"];

            var payment = new Domain.Entities.Payment
            {
                PublicId = Guid.NewGuid(),
                SocietyId = user.SocietyId,
                RecordedBy = userId,
                Amount = serverAmount,
                ModeCode = PaymentModeCodes.Razorpay,
                // Encode planId into Reference so it can be resolved without guessing at verification
                Reference = $"plan:{planId}|order:{razorpayOrderId}",
                CreatedAt = DateTime.UtcNow,
                RazorpayOrderId = razorpayOrderId,
                PaymentType = PaymentTypeCodes.Subscription
            };

            await _paymentRepo.AddAsync(payment);

            _logger.LogInformation("Created Razorpay order {OrderId} for user {UserId}, plan {PlanId}, amount {Amount}",
                razorpayOrderId, userId, planId, serverAmount);

            return new CreateOrderResponse
            {
                OrderId = payment.RazorpayOrderId,
                Amount = payment.Amount,
                Currency = "INR",
                KeyId = _keyId
            };
        }

        /// <summary>
        /// Verifies payment signature and activates subscription. Uses constant-time comparison for security.
        /// </summary>
        public async Task<VerifyPaymentResponse> VerifyPaymentAsync(VerifyPaymentRequest request)
        {
            var payment = await _paymentRepo.GetByRazorpayOrderIdAsync(request.OrderId);
            if (payment == null)
            {
                _logger.LogWarning("VerifyPayment: order {OrderId} not found", request.OrderId);
                return new VerifyPaymentResponse { IsValid = false, Message = "Order not found" };
            }

            // Idempotency: already verified
            if (payment.RazorpayPaymentId != null)
            {
                _logger.LogInformation("VerifyPayment: order {OrderId} already verified", request.OrderId);
                return new VerifyPaymentResponse { IsValid = true, Message = "Payment already verified" };
            }

            // Fix #5: Use constant-time comparison to prevent timing attacks
            var expectedBytes = Encoding.UTF8.GetBytes(GenerateSignature(request.OrderId, request.PaymentId, _keySecret));
            var receivedBytes = Encoding.UTF8.GetBytes(request.Signature);
            var isSignatureValid = expectedBytes.Length == receivedBytes.Length
                                   && CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);

            if (!isSignatureValid)
            {
                // Fix #6: On failure, log only — never mutate DatePaid or store a forged signature
                _logger.LogWarning(
                    "VerifyPayment: invalid signature for order {OrderId}, paymentId {PaymentId}. Possible tampering attempt.",
                    request.OrderId, request.PaymentId);
                return new VerifyPaymentResponse { IsValid = false, Message = "Invalid signature" };
            }

            // Mark as verified — only reached after a valid signature
            payment.RazorpayPaymentId = request.PaymentId;
            payment.RazorpaySignature = request.Signature;
            payment.DatePaid = DateTime.UtcNow;
            payment.VerifiedAt = DateTime.UtcNow;

            await _paymentRepo.UpdateAsync(payment);
            await _paymentRepo.SaveChangesAsync();

            // Fix #2: Resolve plan from the payment record, not by position in a list
            await ActivateSubscriptionAsync(payment, request.PaymentId);

            _logger.LogInformation("Payment verified and subscription activated for order {OrderId}, paymentId {PaymentId}",
                request.OrderId, request.PaymentId);

            return new VerifyPaymentResponse { IsValid = true, Message = "Payment verified and subscription activated" };
        }

        /// <summary>
        /// Initiates a refund for a given Razorpay payment ID.
        /// </summary>
        public async Task<RefundResponse> InitiateRefundAsync(RefundRequest request)
        {
            var payment = await _paymentRepo.GetByRazorpayPaymentIdAsync(request.PaymentId);
            if (payment == null)
                throw new NotFoundException("Payment", request.PaymentId);

            if (request.Amount <= 0 || request.Amount > payment.Amount)
                throw new ValidationException($"Invalid refund amount. Must be between 0 and {payment.Amount}.");

            var client = new RazorpayClient(_keyId, _keySecret);
            var options = new Dictionary<string, object>
            {
                { "amount", (int)(request.Amount * 100) } // Amount in paise
            };

            dynamic refund = null;
            await _razorpayRetry.ExecuteAsync(async ct =>
            {
                refund = await Task.Run(() => client.Payment.Fetch(request.PaymentId).Refund(options), ct);
            });

            var refundId = (string)refund!["id"];
            _logger.LogInformation("Refund initiated for payment {PaymentId}. Refund ID: {RefundId}, Amount: {Amount}",
                request.PaymentId, refundId, request.Amount);

            // The webhook will handle DB updates and business logic.
            return new RefundResponse
            {
                RefundId = refundId,
                PaymentId = request.PaymentId,
                Amount = request.Amount,
                Status = (string)refund!["status"]
            };
        }

        //Accept raw body + signature; verify before processing; idempotency guard
        /// <summary>
        /// Handles Razorpay payment events. Signature is verified server-side using X-Razorpay-Signature header.
        /// </summary>
        public async Task ProcessWebhookAsync(string rawBody, string signature, WebhookPayload payload)
        {
            // Fix #3: Verify Razorpay webhook signature before touching any data
            var expectedBytes = Encoding.UTF8.GetBytes(GenerateWebhookSignature(rawBody, _webhookSecret));
            var receivedBytes = Encoding.UTF8.GetBytes(signature);
            var isSignatureValid = expectedBytes.Length == receivedBytes.Length
                                   && CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);

            if (!isSignatureValid)
            {
                _logger.LogWarning("ProcessWebhook: invalid X-Razorpay-Signature. Possible spoofed webhook.");
                return; // Return 200 to Razorpay so it does not retry; we've rejected the payload silently
            }

            switch (payload.Event)
            {
                case "payment.captured":
                    await ProcessCapturedWebhookAsync(payload);
                    break;
                case "payment.refunded":
                    await ProcessRefundWebhookAsync(rawBody);
                    break;
                default:
                    _logger.LogInformation("ProcessWebhook: ignoring unhandled event '{Event}'", payload.Event);
                    break;
            }
        }

        private async Task ProcessCapturedWebhookAsync(WebhookPayload payload)
        {
            var paymentId = payload.Payment?.Id;
            var orderId = payload.Payment?.OrderId;

            if (string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(orderId))
            {
                _logger.LogWarning("ProcessWebhook(Captured): missing paymentId or orderId in payload");
                return;
            }

            // Fix #4: Double idempotency guard — check both by orderId and by paymentId
            var existingByPaymentId = await _paymentRepo.GetByRazorpayPaymentIdAsync(paymentId);
            if (existingByPaymentId != null)
            {
                _logger.LogInformation("ProcessWebhook(Captured): duplicate webhook for paymentId {PaymentId}, skipping", paymentId);
                return;
            }

            var payment = await _paymentRepo.GetByRazorpayOrderIdAsync(orderId);
            if (payment == null)
            {
                _logger.LogWarning("ProcessWebhook(Captured): no local payment record for orderId {OrderId}", orderId);
                return;
            }

            if (payment.RazorpayPaymentId != null)
            {
                _logger.LogInformation("ProcessWebhook(Captured): orderId {OrderId} already processed, skipping", orderId);
                return;
            }

            payment.RazorpayPaymentId = paymentId;
            payment.DatePaid = DateTime.UtcNow;
            payment.VerifiedAt = DateTime.UtcNow;

            await _paymentRepo.UpdateAsync(payment);
            await _paymentRepo.SaveChangesAsync();

            await ActivateSubscriptionAsync(payment, paymentId);

            _logger.LogInformation("ProcessWebhook(Captured): subscription activated for orderId {OrderId}, paymentId {PaymentId}",
                orderId, paymentId);
        }

        private async Task ProcessRefundWebhookAsync(string rawBody)
        {
            using var jsonDoc = JsonDocument.Parse(rawBody);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("payload", out var payloadElement) ||
                !payloadElement.TryGetProperty("refund", out var refundWrapperElement) ||
                !refundWrapperElement.TryGetProperty("entity", out var refundEntityElement))
            {
                _logger.LogWarning("ProcessWebhook(Refund): event received but refund payload is malformed.");
                return;
            }

            var paymentId = refundEntityElement.TryGetProperty("payment_id", out var pid) ? pid.GetString() : null;
            var refundId = refundEntityElement.TryGetProperty("id", out var rid) ? rid.GetString() : null;
            var refundAmountInPaise = refundEntityElement.TryGetProperty("amount", out var amt) ? amt.GetInt64() : 0;

            if (string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(refundId) || refundAmountInPaise <= 0)
            {
                _logger.LogWarning("ProcessWebhook(Refund): missing or invalid data in refund payload.");
                return;
            }

            var refundAmount = refundAmountInPaise / 100.0m;


            var originalPayment = await _paymentRepo.GetByRazorpayPaymentIdAsync(paymentId);
            if (originalPayment == null)
            {
                _logger.LogWarning("ProcessWebhook(Refund): Original payment {PaymentId} not found for refund {RefundId}", paymentId, refundId);
                return;
            }

            // Create a new Payment record for the refund for audit purposes.
            var refundPayment = new Domain.Entities.Payment
            {
                PublicId = Guid.NewGuid(),
                SocietyId = originalPayment.SocietyId,
                RecordedBy = originalPayment.RecordedBy,
                Amount = refundAmount,
                ModeCode = PaymentModeCodes.Razorpay,
                Reference = $"razorpay_refund_id:{refundId}|original_payment_id:{paymentId}",
                DatePaid = DateTime.UtcNow,
                RazorpayPaymentId = paymentId,
                RazorpayOrderId = originalPayment.RazorpayOrderId,
                PaymentType = "refund",
                VerifiedAt = DateTime.UtcNow
            };

            await _paymentRepo.AddAsync(refundPayment);
            await _paymentRepo.SaveChangesAsync();

            _logger.LogInformation("Recorded refund {RefundId} for payment {PaymentId} of amount {Amount}", refundId, paymentId, refundAmount);

            // If it was a full refund for a subscription, revert the subscription and invoice.
            if (originalPayment.PaymentType == PaymentTypeCodes.Subscription && refundAmount >= originalPayment.Amount)
            {
                await RevertSubscriptionAsync(originalPayment, refundId);
            }
        }

        // Shared subscription activation logic — resolves plan from the stored Reference
        private async Task ActivateSubscriptionAsync(Domain.Entities.Payment payment, string paymentReference)
        {
            var planId = ParsePlanIdFromReference(payment.Reference);
            if (planId == null)
                throw new InvalidOperationException($"Cannot resolve plan from payment reference '{payment.Reference}'");

            var plan = await _planService.GetPlanByIdAsync(planId.Value);
            if (plan == null)
                throw new NotFoundException("Plan", planId.Value.ToString());

            var userId = payment.RecordedBy;
            if (!userId.HasValue || userId.Value <= 0)
            {
                throw new InvalidOperationException($"Payment {payment.Id} has no recorded user. Cannot activate subscription securely.");
            }

            await _subscriptionService.SubscribeAsync(userId.Value, new Application.DTOs.Subscription.SubscribeRequest
            {
                PlanId = plan.Id,
                Amount = payment.Amount,
                PaymentMethod = PaymentModeCodes.Razorpay,
                PaymentReference = paymentReference
            });
        }

        private async Task RevertSubscriptionAsync(Domain.Entities.Payment originalPayment, string refundId)
        {
            if (!originalPayment.RecordedBy.HasValue)
            {
                _logger.LogError("Cannot revert subscription for payment {PaymentId}: RecordedBy user is null.", originalPayment.Id);
                return;
            }

            var userId = originalPayment.RecordedBy.Value;
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
            {
                _logger.LogError("Cannot revert subscription for payment {PaymentId}: User {UserId} not found.", originalPayment.Id, userId);
                return;
            }

            // Revert the associated invoice to Pending
            var invoices = await _invoiceRepo.GetByUserIdAsync(userId);
            var invoice = invoices.FirstOrDefault(i => i.PaymentReference == originalPayment.RazorpayPaymentId);
            if (invoice != null)
            {
                invoice.Status = InvoiceStatusCodes.Pending;
                invoice.PaidDate = null;
                invoice.Description += $"\nPayment refunded on {DateTime.UtcNow:yyyy-MM-dd}. Refund ID: {refundId}.";
                await _invoiceRepo.UpdateAsync(invoice);
                _logger.LogInformation("Invoice {InvoiceId} status reverted to Pending due to refund.", invoice.Id);
            }

            // Cancel the subscription
            await _subscriptionService.CancelSubscriptionAsync(userId, new Application.DTOs.Subscription.CancelSubscriptionRequest
            {
                Reason = $"Payment {originalPayment.RazorpayPaymentId} was fully refunded (Refund ID: {refundId}).",
                CancelImmediately = true
            });
            _logger.LogInformation("Subscription for society {SocietyId} cancelled due to full refund.", user.SocietyId);
        }

        // Reference format: "plan:{guid}|order:{razorpayOrderId}"
        private static Guid? ParsePlanIdFromReference(string? reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            var parts = reference.Split('|');
            foreach (var part in parts)
            {
                if (part.StartsWith("plan:", StringComparison.Ordinal) &&
                    Guid.TryParse(part["plan:".Length..], out var id))
                    return id;
            }
            return null;
        }

        // HMAC for payment signature (orderId|paymentId)
        private static string GenerateSignature(string orderId, string paymentId, string secret)
        {
            var data = $"{orderId}|{paymentId}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return BytesToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }

        // HMAC for webhook signature (raw JSON body)
        private static string GenerateWebhookSignature(string rawBody, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return BytesToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody)));
        }

        private static string BytesToHex(byte[] bytes)
            => BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }
}