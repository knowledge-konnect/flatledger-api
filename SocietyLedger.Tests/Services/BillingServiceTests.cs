using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.Billing;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Services;
using SocietyLedger.Infrastructure.Services.Common;
using Xunit;

namespace SocietyLedger.Tests.Services;

/// <summary>
/// Unit tests for BillingService.GenerateBillsAsync and GenerateMonthlyBillsAsync.
/// All repository / infrastructure dependencies are mocked — no database required.
/// </summary>
public class BillingServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private const long UserId    = 1L;
    private const long SocietyId = 10L;

    private static BillingService Build(
        Mock<IBillRepository>?              billRepo        = null,
        Mock<IFlatRepository>?              flatRepo        = null,
        Mock<ISocietyRepository>?           societyRepo     = null,
        Mock<IMaintenanceConfigRepository>? maintConfigRepo = null,
        Mock<IUserContext>?                 userContext     = null,
        Mock<IDashboardService>?            dashboardService = null,
        Mock<IDapperService>?               dapper          = null)
    {
        billRepo         ??= new Mock<IBillRepository>();
        flatRepo         ??= new Mock<IFlatRepository>();
        societyRepo      ??= new Mock<ISocietyRepository>();
        maintConfigRepo  ??= new Mock<IMaintenanceConfigRepository>();
        userContext       ??= new Mock<IUserContext>();
        dashboardService ??= new Mock<IDashboardService>();
        dapper           ??= new Mock<IDapperService>();

        return new BillingService(
            billRepo.Object,
            flatRepo.Object,
            societyRepo.Object,
            maintConfigRepo.Object,
            userContext.Object,
            Mock.Of<ILogger<BillingService>>(),
            dashboardService.Object,
            dapper.Object);
    }

    private static Flat MakeFlat(long flatId, decimal maintenance = 1500m) => new()
    {
        Id              = flatId,
        PublicId        = Guid.NewGuid(),
        SocietyId       = SocietyId,
        SocietyPublicId = Guid.NewGuid(),
        FlatNo          = $"F{flatId:D3}",
        OwnerName       = "Owner",
        MaintenanceAmount = maintenance,
        StatusName      = string.Empty,
        CreatedAt       = DateTime.UtcNow,
        UpdatedAt       = DateTime.UtcNow
    };

    // ──────────────────────────────────────────────────────────────────────────
    // GenerateBillsAsync — success scenarios
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBillsAsync_ValidPeriod_ReturnsBillsCreatedCount()
    {
        // Arrange
        var period      = DateTime.UtcNow.ToString("yyyy-MM");
        var billRepo    = new Mock<IBillRepository>();
        var flatRepo    = new Mock<IFlatRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var userCtx     = new Mock<IUserContext>();
        var dashboard   = new Mock<IDashboardService>();

        var (user, _) = (new User { Id = UserId }, SocietyId);
        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, period)).ReturnsAsync(false);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        maintRepo.Setup(x => x.GetDefaultChargesBySocietyIdsAsync(It.IsAny<long[]>()))
                 .ReturnsAsync(new Dictionary<long, decimal> { [SocietyId] = 1000m });
        flatRepo.Setup(x => x.GetBySocietyIdAsync(SocietyId))
                .ReturnsAsync(new[] { MakeFlat(1), MakeFlat(2) });
        billRepo.Setup(x => x.AddRangeAndReturnAsync(It.IsAny<IEnumerable<BillAddDto>>()))
                .ReturnsAsync(new List<(long, long)> { (1L, 1L), (2L, 2L) });
        // Re-allocation is a no-op in unit context
        billRepo.Setup(x => x.GetUnpaidBillAmountsAsync(It.IsAny<long>(), It.IsAny<long>()))
                .ReturnsAsync(Array.Empty<(decimal, decimal)>());

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo, userCtx, dashboard);

        // Act
        var result = await service.GenerateBillsAsync(UserId, period);

        // Assert
        result.Should().NotBeNull();
        result.Period.Should().Be(period);
        result.BillsCreated.Should().Be(2);
        billRepo.Verify(x => x.AddRangeAndReturnAsync(It.Is<IEnumerable<BillAddDto>>(b => b.Count() == 2)), Times.Once);
    }

    [Fact]
    public async Task GenerateBillsAsync_FlatWithZeroMaintenance_UsesDefaultChargeAndAddsWarning()
    {
        // Arrange — one flat has maintenance=0 so default charge applies
        var period      = DateTime.UtcNow.ToString("yyyy-MM");
        var billRepo    = new Mock<IBillRepository>();
        var flatRepo    = new Mock<IFlatRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, period)).ReturnsAsync(false);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        maintRepo.Setup(x => x.GetDefaultChargesBySocietyIdsAsync(It.IsAny<long[]>()))
                 .ReturnsAsync(new Dictionary<long, decimal> { [SocietyId] = 0m }); // default is also 0
        flatRepo.Setup(x => x.GetBySocietyIdAsync(SocietyId))
                .ReturnsAsync(new[] { MakeFlat(1, maintenance: 0m) }); // zero maintenance
        billRepo.Setup(x => x.AddRangeAndReturnAsync(It.IsAny<IEnumerable<BillAddDto>>()))
                .ReturnsAsync(new List<(long, long)> { (1L, 1L) });

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo, userCtx);

        // Act
        var result = await service.GenerateBillsAsync(UserId, period);

        // Assert — warning issued for zero-amount bill
        result.Warnings.Should().NotBeNull();
        result.Warnings!.Should().ContainMatch("*billed*₹0*");
    }

    [Fact]
    public async Task GenerateBillsAsync_FlatWithNonZeroMaintenance_NoWarnings()
    {
        var period      = DateTime.UtcNow.ToString("yyyy-MM");
        var billRepo    = new Mock<IBillRepository>();
        var flatRepo    = new Mock<IFlatRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, period)).ReturnsAsync(false);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        maintRepo.Setup(x => x.GetDefaultChargesBySocietyIdsAsync(It.IsAny<long[]>()))
                 .ReturnsAsync(new Dictionary<long, decimal> { [SocietyId] = 1200m });
        flatRepo.Setup(x => x.GetBySocietyIdAsync(SocietyId))
                .ReturnsAsync(new[] { MakeFlat(1, 1500m) });
        billRepo.Setup(x => x.AddRangeAndReturnAsync(It.IsAny<IEnumerable<BillAddDto>>()))
                .ReturnsAsync(new List<(long, long)> { (1L, 1L) });

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo, userCtx);

        var result = await service.GenerateBillsAsync(UserId, period);

        result.Warnings.Should().BeNullOrEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GenerateBillsAsync — duplicate / conflict scenarios
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBillsAsync_PeriodAlreadyExists_ThrowsConflictException()
    {
        // Arrange — bills for this period were already generated
        var period   = "2026-05";
        var billRepo = new Mock<IBillRepository>();
        var userCtx  = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, period)).ReturnsAsync(true);

        var service = Build(billRepo, userContext: userCtx);

        // Act & Assert
        var act = () => service.GenerateBillsAsync(UserId, period);
        await act.Should().ThrowAsync<ConflictException>()
                 .WithMessage("*already been generated*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GenerateBillsAsync — validation: future period guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBillsAsync_PeriodTooFarInFuture_ThrowsValidationException()
    {
        // Arrange — period is 2 months ahead, which exceeds the 1-month limit
        var futurePeriod = DateTime.UtcNow.AddMonths(2).ToString("yyyy-MM");
        var billRepo     = new Mock<IBillRepository>();
        var userCtx      = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, futurePeriod)).ReturnsAsync(false);

        var service = Build(billRepo, userContext: userCtx);

        // Act & Assert
        var act = () => service.GenerateBillsAsync(UserId, futurePeriod);
        await act.Should().ThrowAsync<SocietyLedger.Domain.Exceptions.ValidationException>()
                 .WithMessage("*too far in the future*");
    }

    [Fact]
    public async Task GenerateBillsAsync_PeriodBeforeOnboarding_ThrowsValidationException()
    {
        // Arrange — period is before the society's onboarding date
        var period       = "2023-01";
        var billRepo     = new Mock<IBillRepository>();
        var societyRepo  = new Mock<ISocietyRepository>();
        var userCtx      = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, period)).ReturnsAsync(false);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));

        var service = Build(billRepo, societyRepo: societyRepo, userContext: userCtx);

        // Act & Assert
        var act = () => service.GenerateBillsAsync(UserId, period);
        await act.Should().ThrowAsync<SocietyLedger.Domain.Exceptions.ValidationException>()
                 .WithMessage("*before this society's onboarding date*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GenerateBillsAsync — no flats
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBillsAsync_NoActiveFlatsSociety_ThrowsNotFoundException()
    {
        // Arrange
        var period      = DateTime.UtcNow.ToString("yyyy-MM");
        var billRepo    = new Mock<IBillRepository>();
        var flatRepo    = new Mock<IFlatRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, period)).ReturnsAsync(false);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        maintRepo.Setup(x => x.GetDefaultChargesBySocietyIdsAsync(It.IsAny<long[]>()))
                 .ReturnsAsync(new Dictionary<long, decimal>());
        flatRepo.Setup(x => x.GetBySocietyIdAsync(SocietyId))
                .ReturnsAsync(Array.Empty<Flat>());   // no flats

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo, userCtx);

        // Act & Assert
        var act = () => service.GenerateBillsAsync(UserId, period);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetBillingStatusAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBillingStatusAsync_BillsGenerated_ReturnsGeneratedTrue()
    {
        // Arrange
        var billRepo = new Mock<IBillRepository>();
        var userCtx  = new Mock<IUserContext>();
        var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        billRepo.Setup(x => x.CountForPeriodAsync(SocietyId, currentMonth)).ReturnsAsync(3);

        var service = Build(billRepo, userContext: userCtx);

        // Act
        var result = await service.GetBillingStatusAsync(UserId);

        // Assert
        result.IsGenerated.Should().BeTrue();
        result.GeneratedCount.Should().Be(3);
        result.CurrentMonth.Should().Be(currentMonth);
    }

    [Fact]
    public async Task GetBillingStatusAsync_NoBillsGenerated_ReturnsGeneratedFalse()
    {
        var billRepo     = new Mock<IBillRepository>();
        var userCtx      = new Mock<IUserContext>();
        var currentMonth = DateTime.UtcNow.ToString("yyyy-MM");

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        billRepo.Setup(x => x.CountForPeriodAsync(SocietyId, currentMonth)).ReturnsAsync(0);

        var service = Build(billRepo, userContext: userCtx);

        var result = await service.GetBillingStatusAsync(UserId);

        result.IsGenerated.Should().BeFalse();
        result.GeneratedCount.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GenerateMonthlyBillsAsync — scheduled job
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateMonthlyBillsAsync_NoEligibleSocieties_ReturnsSuccessWithMessage()
    {
        // Arrange — all societies have an onboarding date after the billing period
        var billRepo    = new Mock<IBillRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var flatRepo    = new Mock<IFlatRepository>();

        // Billing month = 2024-01; society onboarded on 2024-02 → ineligible
        var billingMonth = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        societyRepo.Setup(x => x.GetAllActiveOnboardingDatesAsync())
                   .ReturnsAsync(new Dictionary<long, DateOnly> { [SocietyId] = new DateOnly(2024, 2, 1) });

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo);

        // Act
        var result = await service.GenerateMonthlyBillsAsync(billingMonth);

        // Assert
        result.Success.Should().BeTrue();
        result.BillsCreated.Should().Be(0);
        result.ErrorMessage.Should().Contain("No active societies");
    }

    [Fact]
    public async Task GenerateMonthlyBillsAsync_EligibleSocietyWithFlats_CreatesBills()
    {
        // Arrange
        var billingMonth = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var period       = "2026-05";

        var billRepo    = new Mock<IBillRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var flatRepo    = new Mock<IFlatRepository>();

        societyRepo.Setup(x => x.GetAllActiveOnboardingDatesAsync())
                   .ReturnsAsync(new Dictionary<long, DateOnly> { [SocietyId] = new DateOnly(2025, 1, 1) });

        maintRepo.Setup(x => x.GetDefaultChargesBySocietyIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
                 .ReturnsAsync(new Dictionary<long, decimal> { [SocietyId] = 1000m });

        // Two flats eligible for billing
        var flats = new List<FlatBillingInfo>
        {
            new(SocietyId, 1L, 1500m),
            new(SocietyId, 2L, 0m)    // zero → use default
        };
        flatRepo.Setup(x => x.GetActiveFlatsBySocietyIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .ReturnsAsync(flats);

        // No existing bills for this period
        billRepo.Setup(x => x.GetExistingFlatIdsForSocietiesAsync(It.IsAny<IReadOnlyCollection<long>>(), period))
                .ReturnsAsync(Enumerable.Empty<(long, long)>().ToLookup(x => x.Item1, x => x.Item2));
        billRepo.Setup(x => x.AddRangeAndReturnAsync(It.IsAny<IEnumerable<BillAddDto>>()))
                .ReturnsAsync((IEnumerable<BillAddDto> bills) =>
                    bills.Select(b => (b.FlatId, b.FlatId + 100)).ToList());

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo);

        // Act
        var result = await service.GenerateMonthlyBillsAsync(billingMonth);

        // Assert
        result.Success.Should().BeTrue();
        result.BillsCreated.Should().Be(2);
        billRepo.Verify(x => x.AddRangeAndReturnAsync(It.Is<IEnumerable<BillAddDto>>(b => b.Count() == 2)), Times.Once);
    }

    [Fact]
    public async Task GenerateMonthlyBillsAsync_FlatAlreadyBilled_IsSkipped()
    {
        // Arrange — flat 1 already has a bill for this period
        var billingMonth = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var period       = "2026-05";

        var billRepo    = new Mock<IBillRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var flatRepo    = new Mock<IFlatRepository>();

        societyRepo.Setup(x => x.GetAllActiveOnboardingDatesAsync())
                   .ReturnsAsync(new Dictionary<long, DateOnly> { [SocietyId] = new DateOnly(2025, 1, 1) });
        maintRepo.Setup(x => x.GetDefaultChargesBySocietyIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
                 .ReturnsAsync(new Dictionary<long, decimal> { [SocietyId] = 1000m });
        flatRepo.Setup(x => x.GetActiveFlatsBySocietyIdsAsync(It.IsAny<IReadOnlyCollection<long>>()))
                .ReturnsAsync(new List<FlatBillingInfo> { new(SocietyId, 1L, 1500m), new(SocietyId, 2L, 1500m) });

        // Flat 1 already billed
        var existingLookup = new[] { (SocietyId, 1L) }.ToLookup(x => x.Item1, x => x.Item2);
        billRepo.Setup(x => x.GetExistingFlatIdsForSocietiesAsync(It.IsAny<IReadOnlyCollection<long>>(), period))
                .ReturnsAsync(existingLookup);
        billRepo.Setup(x => x.AddRangeAsync(It.IsAny<IEnumerable<BillAddDto>>())).Returns(Task.CompletedTask);

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo);

        // Act
        var result = await service.GenerateMonthlyBillsAsync(billingMonth);

        // Assert — only 1 new bill created (flat 2), flat 1 skipped
        result.BillsCreated.Should().Be(1);
        result.BillsSkipped.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GenerateBillsAsync — bills use flat's maintenance amount when non-zero
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateBillsAsync_FlatWithCustomMaintenance_UsesFlatAmountNotDefault()
    {
        // Arrange
        var period      = DateTime.UtcNow.ToString("yyyy-MM");
        var billRepo    = new Mock<IBillRepository>();
        var flatRepo    = new Mock<IFlatRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var maintRepo   = new Mock<IMaintenanceConfigRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetUserContextAsync(UserId)).ReturnsAsync((new User { Id = UserId }, SocietyId));
        billRepo.Setup(x => x.ExistsForPeriodAsync(SocietyId, period)).ReturnsAsync(false);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        maintRepo.Setup(x => x.GetDefaultChargesBySocietyIdsAsync(It.IsAny<long[]>()))
                 .ReturnsAsync(new Dictionary<long, decimal> { [SocietyId] = 500m });
        // Flat has its own maintenance of 2500 which should override default 500
        flatRepo.Setup(x => x.GetBySocietyIdAsync(SocietyId))
                .ReturnsAsync(new[] { MakeFlat(1, 2500m) });
        billRepo.Setup(x => x.AddRangeAndReturnAsync(It.IsAny<IEnumerable<BillAddDto>>()))
                .ReturnsAsync(new List<(long, long)> { (1L, 1L) });

        var service = Build(billRepo, flatRepo, societyRepo, maintRepo, userCtx);

        // Act
        await service.GenerateBillsAsync(UserId, period);

        // Assert — bill amount should be the flat's maintenance (2500), not default (500)
        billRepo.Verify(x => x.AddRangeAndReturnAsync(
            It.Is<IEnumerable<BillAddDto>>(bills => bills.Single().Amount == 2500m)), Times.Once);
    }
}
