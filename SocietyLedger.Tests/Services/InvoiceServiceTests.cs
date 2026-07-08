using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SocietyLedger.Application.DTOs.Invoice;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Entities;
using SocietyLedger.Infrastructure.Services;
using SocietyLedger.Infrastructure.Services.Common;
using Xunit;
using System.Linq;
using System.Text;

namespace SocietyLedger.Tests.Services;

public class InvoiceServiceTests
{
    private static InvoiceService BuildService(
        Mock<IInvoiceRepository>? invoiceRepo = null,
        Mock<ISubscriptionEventRepository>? eventRepo = null,
        Mock<IUserContext>? userContext = null)
    {
        return new InvoiceService(
            invoiceRepo?.Object ?? new Mock<IInvoiceRepository>().Object,
            eventRepo?.Object ?? new Mock<ISubscriptionEventRepository>().Object,
            userContext?.Object ?? new Mock<IUserContext>().Object,
            NullLogger<InvoiceService>.Instance);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetUserInvoicesAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserInvoicesAsync_ReturnsAllInvoicesMappedToResponse()
    {
        // Arrange
        var invoices = new List<Invoice>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = 1,
                InvoiceNumber = "INV-001",
                InvoiceType = PaymentTypeCodes.Subscription,
                Amount = 499,
                TotalAmount = 499,
                Currency = "INR",
                Status = InvoiceStatusCodes.Paid,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
            },
            new()
            {
                Id = Guid.NewGuid(),
                UserId = 1,
                InvoiceNumber = "INV-002",
                InvoiceType = PaymentTypeCodes.Subscription,
                Amount = 999,
                TotalAmount = 999,
                Currency = "INR",
                Status = InvoiceStatusCodes.Pending,
                DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
            }
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(invoices);

        var svc = BuildService(invoiceRepo: invoiceRepo);

        // Act
        var result = (await svc.GetUserInvoicesAsync(1)).ToList();

        // Assert
        result.Should().HaveCount(2);
        result[0].InvoiceNumber.Should().Be("INV-001");
        result[1].Status.Should().Be(InvoiceStatusCodes.Pending);
    }

    [Fact]
    public async Task GetUserInvoicesAsync_NoInvoices_ReturnsEmpty()
    {
        // Arrange
        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(Enumerable.Empty<Invoice>());

        var svc = BuildService(invoiceRepo: invoiceRepo);

        // Act
        var result = await svc.GetUserInvoicesAsync(1);

        // Assert
        result.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PayInvoiceAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PayInvoiceAsync_InvoiceNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Invoice?)null);

        var svc = BuildService(invoiceRepo: invoiceRepo);

        // Act
        var act = () => svc.PayInvoiceAsync(Guid.NewGuid(), 1,
            new PayInvoiceRequest { PaymentMethod = "upi" });

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Invoice*");
    }

    [Fact]
    public async Task PayInvoiceAsync_CrossSocietyAccess_ThrowsAuthorizationException()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 99, // owner belongs to society 99
            Status = InvoiceStatusCodes.Pending,
            TotalAmount = 499,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);   // caller → society 10
        userContext.Setup(c => c.GetSocietyIdAsync(99)).ReturnsAsync(20L);  // owner → society 20

        var svc = BuildService(invoiceRepo: invoiceRepo, userContext: userContext);

        // Act
        var act = () => svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "upi" });

        // Assert
        await act.Should().ThrowAsync<AuthorizationException>()
            .WithMessage("*permission*");
    }

    [Fact]
    public async Task PayInvoiceAsync_AlreadyPaid_ThrowsConflictException()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 1,
            Status = InvoiceStatusCodes.Paid,
            TotalAmount = 499,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);

        var svc = BuildService(invoiceRepo: invoiceRepo, userContext: userContext);

        // Act
        var act = () => svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "upi" });

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already paid*");
    }

    [Fact]
    public async Task PayInvoiceAsync_PartialPaymentBelowTotal_ThrowsValidationException()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 1,
            Status = InvoiceStatusCodes.Pending,
            TotalAmount = 499,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);

        var svc = BuildService(invoiceRepo: invoiceRepo, userContext: userContext);

        // Act
        var act = () => svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "upi", Amount = 200 }); // less than 499

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*200*499*");
    }

    [Fact]
    public async Task PayInvoiceAsync_ValidPayment_UpdatesInvoiceAndReturnsResponse()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 1,
            InvoiceNumber = "INV-001",
            InvoiceType = PaymentTypeCodes.Subscription,
            Amount = 499,
            TotalAmount = 499,
            Currency = "INR",
            Status = InvoiceStatusCodes.Pending,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);

        var svc = BuildService(invoiceRepo: invoiceRepo, userContext: userContext);

        // Act
        var result = await svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "bank_transfer", PaymentReference = "REF-123" });

        // Assert
        result.Status.Should().Be(InvoiceStatusCodes.Paid);
        result.PaymentMethod.Should().Be("bank_transfer");
        result.PaymentReference.Should().Be("REF-123");
        result.PaidDate.Should().NotBeNull();

        invoiceRepo.Verify(r => r.UpdateAsync(It.Is<Invoice>(i => i.Status == InvoiceStatusCodes.Paid)), Times.Once);
    }

    [Fact]
    public async Task PayInvoiceAsync_SubscriptionInvoice_CreatesSubscriptionEvent()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 1,
            SubscriptionId = subscriptionId,
            InvoiceType = PaymentTypeCodes.Subscription,
            Amount = 499,
            TotalAmount = 499,
            Status = InvoiceStatusCodes.Pending,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);

        var eventRepo = new Mock<ISubscriptionEventRepository>();

        var svc = BuildService(invoiceRepo: invoiceRepo, eventRepo: eventRepo, userContext: userContext);

        // Act
        await svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "upi" });

        // Assert
        eventRepo.Verify(r => r.CreateAsync(It.Is<SubscriptionEvent>(e =>
            e.EventType == "payment_received" &&
            e.SubscriptionId == subscriptionId &&
            e.Amount == 499)), Times.Once);
    }

    [Fact]
    public async Task PayInvoiceAsync_NonSubscriptionInvoice_DoesNotCreateEvent()
    {
        // Arrange — SubscriptionId is null → no event should be raised
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 1,
            SubscriptionId = null,
            InvoiceType = "other",
            Amount = 100,
            TotalAmount = 100,
            Status = InvoiceStatusCodes.Pending,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);

        var eventRepo = new Mock<ISubscriptionEventRepository>();

        var svc = BuildService(invoiceRepo: invoiceRepo, eventRepo: eventRepo, userContext: userContext);

        // Act
        await svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "cash" });

        // Assert
        eventRepo.Verify(r => r.CreateAsync(It.IsAny<SubscriptionEvent>()), Times.Never);
    }

    [Fact]
    public async Task PayInvoiceAsync_AmountEqualsTotal_IsAllowed()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 1,
            Amount = 499,
            TotalAmount = 499,
            Status = InvoiceStatusCodes.Pending,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);

        var svc = BuildService(invoiceRepo: invoiceRepo, userContext: userContext);

        // Act
        var act = () => svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "upi", Amount = 499 }); // exact match

        // Assert — should NOT throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PayInvoiceAsync_SameSocietyDifferentUser_IsAllowed()
    {
        // Arrange — caller (userId=1, society=10) paying invoice owned by userId=2 (also society=10)
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 2,
            Amount = 499,
            TotalAmount = 499,
            Status = InvoiceStatusCodes.Pending,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);
        userContext.Setup(c => c.GetSocietyIdAsync(2)).ReturnsAsync(10L);

        var svc = BuildService(invoiceRepo: invoiceRepo, userContext: userContext);

        // Act
        var act = () => svc.PayInvoiceAsync(invoiceId, 1,
            new PayInvoiceRequest { PaymentMethod = "cash" });

        // Assert — same society, should succeed
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GenerateInvoicePdfAsync_ReturnsPdfBytesAndFilename()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            UserId = 2,
            InvoiceNumber = "INV-1001",
            InvoiceType = PaymentTypeCodes.Subscription,
            Amount = 499,
            TotalAmount = 499,
            Currency = "INR",
            Status = InvoiceStatusCodes.Paid,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Description = "Subscription to Pro plan"
        };

        var invoiceRepo = new Mock<IInvoiceRepository>();
        invoiceRepo.Setup(r => r.GetByIdAsync(invoiceId)).ReturnsAsync(invoice);

        var userContext = new Mock<IUserContext>();
        userContext.Setup(c => c.GetSocietyIdAsync(1)).ReturnsAsync(10L);
        userContext.Setup(c => c.GetSocietyIdAsync(2)).ReturnsAsync(10L);

        var svc = BuildService(invoiceRepo: invoiceRepo, userContext: userContext);

        // Act
        var (bytes, fileName) = await svc.GenerateInvoicePdfAsync(invoiceId, 1);

        // Assert
        bytes.Should().NotBeEmpty();
        fileName.Should().Contain("INV-1001");
        fileName.Should().EndWith(".pdf");
        Encoding.ASCII.GetString(bytes.Take(5).ToArray()).Should().Be("%PDF-");
    }
}
