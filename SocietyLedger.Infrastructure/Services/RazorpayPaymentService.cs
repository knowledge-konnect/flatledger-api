using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Razorpay.Api;
using SocietyLedger.Application.DTOs.Invoice;
using SocietyLedger.Application.DTOs.Razorpay;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using System.Security.Cryptography;
using System.Text;

namespace SocietyLedger.Infrastructure.Services
{
    public class RazorpayPaymentService : IRazorpayPaymentService
    {
        private readonly IPaymentRepository _paymentRepo;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IInvoiceService _invoiceService;
        private readonly IPlanService _planService;
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
            IInvoiceService invoiceService,
            IPlanService planService,
            IUserRepository userRepo,
            ILogger<RazorpayPaymentService> logger,
            IConfiguration config)
        {
            _paymentRepo = paymentRepo;
            _subscriptionService = subscriptionService;
            _invoiceService = invoiceService;
            _planService = planService;
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

            // Reuse a recent pending order only when it is for the same plan — if the user
            // switched plans the old order must not be recycled or the wrong plan activates.
            var existingPending = await _paymentRepo.GetPendingSubscriptionPaymentByUserIdAsync(userId);
            if (existingPending != null
                && existingPending.CreatedAt >= DateTime.UtcNow - OrderExpiry
                && ParsePlanIdFromReference(existingPending.Reference) == planId)
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

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null)
                throw new NotFoundException("User", userId.ToString());

            var client = new RazorpayClient(_keyId, _keySecret);
            var serverAmount = plan.Price;

            var options = new Dictionary<string, object>
            {
                { "amount", (int)(serverAmount * 100) },
                { "currency", "INR" },
                { "receipt", $"receipt_{userId}_{DateTime.UtcNow.Ticks}" }
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
                Amount = serverAmount,
                ModeCode = PaymentModeCodes.Razorpay,
                // Encode planId into Reference so it can be resolved without guessing at verification
                Reference = $"plan:{planId}|order:{razorpayOrderId}",
                CreatedAt = DateTime.UtcNow,
                // Record which user initiated this order so activation can run in the correct user context
                RecordedBy = userId,
                RazorpayOrderId = razorpayOrderId,
                PaymentType = PaymentTypeCodes.Subscription
            };

            await _paymentRepo.AddAsync(payment);
            await _paymentRepo.SaveChangesAsync();

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
        /// Verifies payment signature and activates subscription.
        /// Fix #11: advisory lock on orderId prevents concurrent activation when both
        /// VerifyPaymentAsync and ProcessWebhookAsync fire at the same time.
        /// </summary>
        public async Task<VerifyPaymentResponse> VerifyPaymentAsync(VerifyPaymentRequest request)
        {
            var payment = await _paymentRepo.GetByRazorpayOrderIdAsync(request.OrderId);
            if (payment == null)
            {
                _logger.LogWarning("VerifyPayment: order {OrderId} not found", request.OrderId);
                return new VerifyPaymentResponse { IsValid = false, Message = "Order not found" };
            }

            // Fast-path idempotency check before acquiring the lock
            if (payment.RazorpayPaymentId != null)
            {
                _logger.LogInformation("VerifyPayment: order {OrderId} already verified", request.OrderId);
                return new VerifyPaymentResponse { IsValid = true, Message = "Payment already verified" };
            }

            var expectedBytes = Encoding.UTF8.GetBytes(GenerateSignature(request.OrderId, request.PaymentId, _keySecret));
            var receivedBytes = Encoding.UTF8.GetBytes(request.Signature);
            var isSignatureValid = expectedBytes.Length == receivedBytes.Length
                                   && CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);

            if (!isSignatureValid)
            {
                _logger.LogWarning(
                    "VerifyPayment: invalid signature for order {OrderId}, paymentId {PaymentId}. Possible tampering attempt.",
                    request.OrderId, request.PaymentId);
                return new VerifyPaymentResponse { IsValid = false, Message = "Invalid signature" };
            }

            // Fix #11: advisory lock keyed by orderId hash — serialises concurrent verify + webhook
            var lockKey = (long)(uint)request.OrderId.GetHashCode();
            await _paymentRepo.ExecuteWithAdvisoryLockAsync(lockKey, async () =>
            {
                // Re-read inside the lock — webhook may have already processed this
                var freshPayment = await _paymentRepo.GetByRazorpayOrderIdAsync(request.OrderId);
                if (freshPayment?.RazorpayPaymentId != null)
                {
                    _logger.LogInformation("VerifyPayment: order {OrderId} already processed (concurrent webhook), skipping", request.OrderId);
                    return;
                }

                payment.RazorpayPaymentId = request.PaymentId;
                payment.RazorpaySignature = request.Signature;
                payment.DatePaid = DateTime.UtcNow;
                payment.VerifiedAt = DateTime.UtcNow;

                await _paymentRepo.UpdateAsync(payment);
                await _paymentRepo.SaveChangesAsync();

                await ActivateSubscriptionAsync(payment, request.PaymentId);
            });

            _logger.LogInformation("Payment verified and subscription activated for order {OrderId}, paymentId {PaymentId}",
                request.OrderId, request.PaymentId);

            return new VerifyPaymentResponse { IsValid = true, Message = "Payment verified and subscription activated" };
        }

        public async Task ProcessWebhookAsync(string rawBody, string signature, WebhookPayload payload)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(GenerateWebhookSignature(rawBody, _webhookSecret));
            var receivedBytes = Encoding.UTF8.GetBytes(signature);
            var isSignatureValid = expectedBytes.Length == receivedBytes.Length
                                   && CryptographicOperations.FixedTimeEquals(expectedBytes, receivedBytes);

            if (!isSignatureValid)
            {
                _logger.LogWarning("ProcessWebhook: invalid X-Razorpay-Signature. Possible spoofed webhook.");
                return;
            }

            if (payload.Event != "payment.captured")
            {
                _logger.LogInformation("ProcessWebhook: ignoring unhandled event '{Event}'", payload.Event);
                return;
            }

            var paymentId = payload.Payment?.Id;
            var orderId = payload.Payment?.OrderId;

            if (string.IsNullOrEmpty(paymentId) || string.IsNullOrEmpty(orderId))
            {
                _logger.LogWarning("ProcessWebhook: missing paymentId or orderId in payload");
                return;
            }

            // Fast-path: already processed by paymentId
            var existingByPaymentId = await _paymentRepo.GetByRazorpayPaymentIdAsync(paymentId);
            if (existingByPaymentId != null)
            {
                _logger.LogInformation("ProcessWebhook: duplicate webhook for paymentId {PaymentId}, skipping", paymentId);
                return;
            }

            // Fix #11: same advisory lock key as VerifyPaymentAsync — serialises concurrent processing
            var lockKey = (long)(uint)orderId.GetHashCode();
            await _paymentRepo.ExecuteWithAdvisoryLockAsync(lockKey, async () =>
            {
                // Re-read inside the lock
                var payment = await _paymentRepo.GetByRazorpayOrderIdAsync(orderId);
                if (payment == null)
                {
                    _logger.LogWarning("ProcessWebhook: no local payment record for orderId {OrderId}", orderId);
                    return;
                }

                if (payment.RazorpayPaymentId != null)
                {
                    _logger.LogInformation("ProcessWebhook: orderId {OrderId} already processed, skipping", orderId);
                    return;
                }

                payment.RazorpayPaymentId = paymentId;
                payment.DatePaid = DateTime.UtcNow;
                payment.VerifiedAt = DateTime.UtcNow;

                await _paymentRepo.UpdateAsync(payment);
                await _paymentRepo.SaveChangesAsync();

                await ActivateSubscriptionAsync(payment, paymentId);

                _logger.LogInformation("ProcessWebhook: subscription activated for orderId {OrderId}, paymentId {PaymentId}",
                    orderId, paymentId);
            });
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

            // Use the user who created the order as the activating user so SubscribeAsync
            // can resolve the correct society context. RecordedBy should always be set
            // by CreateOrderAsync; fail-fast if it's missing to avoid subscribing under
            // the wrong identity.
            if (!payment.RecordedBy.HasValue)
                throw new InvalidOperationException($"Cannot activate subscription for order {payment.RazorpayOrderId}: RecordedBy is not set.");

            var subscribeResponse = await _subscriptionService.SubscribeAsync(payment.RecordedBy.Value, new Application.DTOs.Subscription.SubscribeRequest
            {
                PlanId = plan.Id,
                PaymentMethod = PaymentModeCodes.Razorpay,
                PaymentReference = paymentReference
            });

            // Mark the invoice Paid immediately — SubscribeAsync creates it as Pending for Razorpay.
            // Using the internal (non-IDOR) path because the payment is already verified server-side.
            await _invoiceService.PayInvoiceAsync(
                subscribeResponse.InvoiceId,
                payment.RecordedBy.Value,
                new PayInvoiceRequest
                {
                    PaymentMethod = PaymentModeCodes.Razorpay,
                    PaymentReference = paymentReference
                });

            _logger.LogInformation(
                "Invoice {InvoiceId} marked Paid for order {OrderId}",
                subscribeResponse.InvoiceId, payment.RazorpayOrderId);
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

        // Fix #5: HMAC for payment signature (orderId|paymentId)
        private static string GenerateSignature(string orderId, string paymentId, string secret)
        {
            var data = $"{orderId}|{paymentId}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return BytesToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }

        // Fix #3: HMAC for webhook signature (raw JSON body)
        private static string GenerateWebhookSignature(string rawBody, string secret)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return BytesToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody)));
        }

        private static string BytesToHex(byte[] bytes)
            => BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }
}