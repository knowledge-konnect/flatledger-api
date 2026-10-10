using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.MaintenancePayment;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Application.Validators.MaintenancePayment;
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
            Category: null,
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
            Category: null,
            IdempotencyKey: null);

        var act = () => service.ProcessPaymentAsync(request, UserId);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public void CreateMaintenancePaymentRequestValidator_AllowsMinimalIncomeCategories()
    {
        var validator = new CreateMaintenancePaymentRequestValidator();

        foreach (var category in new[]
                 {
                     "moving_charges",
                     "parking_income",
                     "bank_interest",
                     "other_income",
                     "lift_usage_charges"
                 })
        {
            var request = new CreateMaintenancePaymentRequest(
                FlatPublicId: Guid.NewGuid(),
                Amount: 100m,
                PaymentDate: DateTime.UtcNow,
                PaymentModeCode: "CASH",
                ReferenceNumber: null,
                ReceiptUrl: null,
                Notes: null,
                Category: category);

            validator.Validate(request).IsValid.Should().BeTrue($"'{category}' should be accepted as an income category.");
        }

        var legacyRequest = new CreateMaintenancePaymentRequest(
            FlatPublicId: Guid.NewGuid(),
            Amount: 100m,
            PaymentDate: DateTime.UtcNow,
            PaymentModeCode: "CASH",
            ReferenceNumber: null,
            ReceiptUrl: null,
            Notes: null,
            Category: "lift_shift_fee");

        validator.Validate(legacyRequest).IsValid.Should().BeFalse("legacy income categories should no longer be accepted.");
    }

    [Fact]
    public void MaintenanceSummaryQuery_CountsOnlyBillLinkedCollectionsForTheSelectedPeriod()
    {
        var sql = SqlQueries.MaintenanceSummary;

        sql.Should().Contain("JOIN bills b ON b.id = mp.bill_id");
        sql.Should().Contain("AND b.period = @Period");
        sql.Should().Contain("AND mp.bill_id IS NOT NULL");
    }

    [Fact]
    public void BillStatusRecalculation_PrioritizesOverdue_OverPartial()
    {
        var updateSql = SqlQueries.UpdateBillPayment;
        var deleteSql = SqlQueries.RecalculateBillAfterPaymentDelete;

        var partialIndex = updateSql.IndexOf("WHEN @PaidAmount > 0 AND (due_date IS NULL OR due_date >= NOW())", StringComparison.Ordinal);
        var overdueIndex = updateSql.IndexOf("WHEN due_date IS NOT NULL AND due_date < NOW()", StringComparison.Ordinal);

        partialIndex.Should().BeGreaterThan(-1);
        overdueIndex.Should().BeGreaterThan(-1);
        overdueIndex.Should().BeLessThan(partialIndex);

        var deletePartialIndex = deleteSql.IndexOf("THEN 'partial'", StringComparison.Ordinal);
        var deleteOverdueIndex = deleteSql.IndexOf("WHEN due_date IS NOT NULL AND due_date < NOW()", StringComparison.Ordinal);

        deletePartialIndex.Should().BeGreaterThan(-1);
        deleteOverdueIndex.Should().BeGreaterThan(-1);
        deleteOverdueIndex.Should().BeLessThan(deletePartialIndex);
    }

    [Fact]
    public void LockUnpaidBillsByFlat_OrdersOldestOutstandingBillsFirst()
    {
        var sql = SqlQueries.LockUnpaidBillsByFlat;

        sql.Should().Contain("ORDER  BY b.due_date ASC NULLS LAST");
        sql.Should().Contain("b.period ASC");
    }
}
