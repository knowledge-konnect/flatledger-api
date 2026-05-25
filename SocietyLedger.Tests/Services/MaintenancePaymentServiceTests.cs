using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.MaintenancePayment;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services;
using SocietyLedger.Infrastructure.Services.Common;
using Xunit;

namespace SocietyLedger.Tests.Services;

public class MaintenancePaymentServiceTests
{
    private const long UserId = 1L;
    private const long SocietyId = 10L;

    private static MaintenancePaymentService Build(
        Mock<IUserContext>? userContext = null,
        Mock<IDapperService>? dapper = null)
    {
        userContext ??= new Mock<IUserContext>();
        dapper ??= new Mock<IDapperService>();

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        return new MaintenancePaymentService(
            Mock.Of<IMaintenancePaymentRepository>(),
            Mock.Of<IPaymentModeRepository>(),
            Mock.Of<ISocietyRepository>(),
            userContext.Object,
            new AppDbContext(opts),
            dapper.Object,
            Mock.Of<IDashboardService>(),
            Mock.Of<ILogger<MaintenancePaymentService>>());
    }

    [Fact]
    public async Task ProcessPaymentAsync_ZeroAmount_ThrowsValidationException()
    {
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);

        var service = Build(userContext);
        var request = new MaintenancePaymentRequest(
            FlatPublicId: Guid.NewGuid(),
            Amount: 0m,
            PaymentDate: DateTime.UtcNow,
            PaymentModeCode: "CASH",
            ReferenceNumber: null,
            ReceiptUrl: null,
            Notes: null,
            IdempotencyKey: null);

        var act = () => service.ProcessPaymentAsync(request, UserId);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*positive*");
    }

    [Fact]
    public async Task ProcessPaymentAsync_NegativeAmount_ThrowsValidationException()
    {
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);

        var service = Build(userContext);
        var request = new MaintenancePaymentRequest(
            FlatPublicId: Guid.NewGuid(),
            Amount: -100m,
            PaymentDate: DateTime.UtcNow,
            PaymentModeCode: "CASH",
            ReferenceNumber: null,
            ReceiptUrl: null,
            Notes: null,
            IdempotencyKey: null);

        var act = () => service.ProcessPaymentAsync(request, UserId);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
