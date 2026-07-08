using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs.Invoice;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Infrastructure.Services.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SocietyLedger.Infrastructure.Services
{
    public class InvoiceService : IInvoiceService
    {
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ISubscriptionEventRepository _eventRepo;
        private readonly IUserContext _userContext;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(
            IInvoiceRepository invoiceRepo,
            ISubscriptionEventRepository eventRepo,
            IUserContext userContext,
            ILogger<InvoiceService> logger)
        {
            _invoiceRepo = invoiceRepo;
            _eventRepo = eventRepo;
            _userContext = userContext;
            _logger = logger;
        }

        public async Task<IEnumerable<InvoiceResponse>> GetUserInvoicesAsync(long userId)
        {
            var invoices = await _invoiceRepo.GetByUserIdAsync(userId);
            return invoices.Select(i => new InvoiceResponse
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                InvoiceType = i.InvoiceType,
                Amount = i.Amount,
                TaxAmount = i.TaxAmount,
                TotalAmount = i.TotalAmount,
                Currency = i.Currency,
                Status = i.Status,
                PeriodStart = i.PeriodStart,
                PeriodEnd = i.PeriodEnd,
                DueDate = i.DueDate,
                PaidDate = i.PaidDate,
                PaymentMethod = i.PaymentMethod,
                PaymentReference = i.PaymentReference,
                Description = i.Description,
                CreatedAt = i.CreatedAt
            });
        }

        public async Task<InvoiceResponse> PayInvoiceAsync(Guid invoiceId, long userId, PayInvoiceRequest request)
        {
            var invoice = await _invoiceRepo.GetByIdAsync(invoiceId);
            if (invoice == null)
                throw new NotFoundException("Invoice", invoiceId.ToString());

            // Society-scoped ownership: any admin of the same society can pay an invoice,
            // not just the specific user who created the subscription.
            var callerSocietyId = await _userContext.GetSocietyIdAsync(userId);
            var ownerSocietyId  = await _userContext.GetSocietyIdAsync(invoice.UserId);
            if (callerSocietyId != ownerSocietyId)
                throw new AuthorizationException("You do not have permission to pay this invoice.");

            if (invoice.Status == InvoiceStatusCodes.Paid)
                throw new ConflictException("Invoice is already paid");

            if (request.Amount.HasValue && request.Amount.Value < invoice.TotalAmount)
                throw new ValidationException(
                    $"Payment amount ({request.Amount.Value:F2}) does not cover the invoice total ({invoice.TotalAmount:F2}).");

            var now = DateTime.UtcNow;
            var amount = request.Amount ?? invoice.TotalAmount;

            invoice.Status = InvoiceStatusCodes.Paid;
            invoice.PaidDate = now;
            invoice.PaymentMethod = request.PaymentMethod;
            invoice.PaymentReference = request.PaymentReference;
            invoice.UpdatedAt = now;

            await _invoiceRepo.UpdateAsync(invoice);

            // Create subscription event if this is a subscription invoice
            if (invoice.SubscriptionId.HasValue)
            {
                // Use JsonSerializer — never interpolate user-supplied values into JSON strings.
                var eventMeta = JsonSerializer.Serialize(new
                {
                    invoice_id = invoiceId,
                    payment_method = request.PaymentMethod
                });

                await _eventRepo.CreateAsync(new SubscriptionEvent
                {
                    Id = Guid.NewGuid(),
                    UserId = invoice.UserId,
                    SubscriptionId = invoice.SubscriptionId,
                    EventType = "payment_received",
                    Amount = amount,
                    Metadata = eventMeta
                });
            }

            _logger.LogInformation("Invoice {InvoiceId} paid by user {UserId}", invoiceId, invoice.UserId);

            return new InvoiceResponse
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceType = invoice.InvoiceType,
                Amount = invoice.Amount,
                TaxAmount = invoice.TaxAmount,
                TotalAmount = invoice.TotalAmount,
                Currency = invoice.Currency,
                Status = invoice.Status,
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                DueDate = invoice.DueDate,
                PaidDate = invoice.PaidDate,
                PaymentMethod = invoice.PaymentMethod,
                PaymentReference = invoice.PaymentReference,
                Description = invoice.Description,
                CreatedAt = invoice.CreatedAt
            };
        }

        public async Task<(byte[] Bytes, string FileName)> GenerateInvoicePdfAsync(Guid invoiceId, long userId)
        {
            var invoice = await _invoiceRepo.GetByIdAsync(invoiceId);
            if (invoice == null)
                throw new NotFoundException("Invoice", invoiceId.ToString());

            var callerSocietyId = await _userContext.GetSocietyIdAsync(userId);
            var ownerSocietyId = await _userContext.GetSocietyIdAsync(invoice.UserId);
            if (callerSocietyId != ownerSocietyId)
                throw new AuthorizationException("You do not have permission to download this invoice.");

            var bytes = BuildInvoicePdf(invoice);
            var safeInvoiceNumber = (invoice.InvoiceNumber ?? invoice.Id.ToString()[..8]).Replace('/', '-').Replace('\\', '-');
            var fileName = $"invoice-{safeInvoiceNumber}.pdf";

            return (bytes, fileName);
        }

        private static byte[] BuildInvoicePdf(Invoice invoice)
        {
            var invoiceNumber = invoice.InvoiceNumber ?? invoice.Id.ToString()[..8];
            var currency = string.IsNullOrWhiteSpace(invoice.Currency) ? "INR" : invoice.Currency;
            var amount = invoice.TotalAmount > 0 ? invoice.TotalAmount : invoice.Amount;

            var lines = new List<string>
            {
                "FlatLedger - Subscription Invoice",
                $"Invoice Number: {invoiceNumber}",
                $"Invoice Type: {invoice.InvoiceType}",
                $"Status: {invoice.Status}",
                $"Amount: {amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}",
                $"Due Date: {invoice.DueDate:yyyy-MM-dd}",
                $"Paid Date: {(invoice.PaidDate.HasValue ? invoice.PaidDate.Value.ToString("yyyy-MM-dd") : "Not paid yet")}",
                $"Payment Method: {(string.IsNullOrWhiteSpace(invoice.PaymentMethod) ? "-" : invoice.PaymentMethod)}",
                $"Reference: {(string.IsNullOrWhiteSpace(invoice.PaymentReference) ? "-" : invoice.PaymentReference)}",
                $"Description: {(string.IsNullOrWhiteSpace(invoice.Description) ? "-" : invoice.Description)}"
            };

            var content = new StringBuilder();
            content.Append("BT\n/F1 12 Tf\n50 760 Td\n");
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    content.Append("0 -18 Td\n");
                }
                content.Append('(');
                content.Append(EscapePdfText(lines[i]));
                content.Append(") Tj\n");
            }
            content.Append("ET\n");

            var contentBytes = Encoding.ASCII.GetBytes(content.ToString());

            var objects = new List<byte[]>
            {
                Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
                Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
                BuildStreamObject(contentBytes),
                Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            };

            using var ms = new MemoryStream();
            WriteAscii(ms, "%PDF-1.4\n");

            var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                WriteAscii(ms, $"{i + 1} 0 obj\n");
                ms.Write(objects[i], 0, objects[i].Length);
                WriteAscii(ms, "\nendobj\n");
            }

            var xrefOffset = ms.Position;
            WriteAscii(ms, $"xref\n0 {objects.Count + 1}\n");
            WriteAscii(ms, "0000000000 65535 f \n");

            for (var i = 1; i < offsets.Count; i++)
            {
                WriteAscii(ms, $"{offsets[i]:0000000000} 00000 n \n");
            }

            WriteAscii(ms, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\n");
            WriteAscii(ms, $"startxref\n{xrefOffset}\n%%EOF");

            return ms.ToArray();
        }

        private static byte[] BuildStreamObject(byte[] contentBytes)
        {
            using var ms = new MemoryStream();
            WriteAscii(ms, $"<< /Length {contentBytes.Length} >>\nstream\n");
            ms.Write(contentBytes, 0, contentBytes.Length);
            WriteAscii(ms, "endstream");
            return ms.ToArray();
        }

        private static string EscapePdfText(string input)
        {
            return input
                .Replace("\\", "\\\\")
                .Replace("(", "\\(")
                .Replace(")", "\\)");
        }

        private static void WriteAscii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
