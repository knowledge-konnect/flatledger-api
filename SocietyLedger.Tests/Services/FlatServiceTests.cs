using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.Flat;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services;
using SocietyLedger.Infrastructure.Services.Common;
using Xunit;

namespace SocietyLedger.Tests.Services;

/// <summary>
/// Unit tests for FlatService covering: create, update, delete, bulk-create, get by id.
/// AppDbContext is mocked at the EF level using InMemory provider to avoid touching
/// the helpers (ComputeOutstandingByFlatIdAsync) that use _db directly.
/// NOTE: BulkCreateAsync directly queries _db.flats / _db.flat_statuses so those methods
/// cannot be tested without an integration database. They are excluded here and covered
/// under BulkCreate tests that use skipBilling=true and a pre-seeded InMemory context.
/// </summary>
public class FlatServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly long DefaultUserId   = 1L;
    private static readonly long DefaultSocietyId = 10L;

    private static Flat MakeFlat(string flatNo = "A101", long societyId = 10,
        string? email = null, string? mobile = null) => new()
    {
        Id              = 1,
        PublicId        = Guid.NewGuid(),
        SocietyId       = societyId,
        SocietyPublicId = Guid.NewGuid(),
        FlatNo          = flatNo,
        OwnerName       = "Test Owner",
        ContactEmail    = email,
        ContactMobile   = mobile,
        MaintenanceAmount = 1000m,
        StatusName      = string.Empty,
        CreatedAt       = DateTime.UtcNow,
        UpdatedAt       = DateTime.UtcNow
    };

    private static FlatService BuildService(
        Mock<IFlatRepository>? flatRepo                        = null,
        Mock<IBillRepository>? billRepo                        = null,
        Mock<IUserContext>? userContext                        = null,
        Mock<IMaintenanceConfigRepository>? maintConfigRepo   = null,
        Mock<IBillingService>? billingService                  = null,
        Mock<IAdjustmentRepository>? adjustmentRepo            = null,
        Mock<IMaintenancePaymentRepository>? mpRepo            = null,
        AppDbContext? db                                        = null)
    {
        flatRepo        ??= new Mock<IFlatRepository>();
        billRepo        ??= new Mock<IBillRepository>();
        userContext     ??= new Mock<IUserContext>();
        maintConfigRepo ??= new Mock<IMaintenanceConfigRepository>();
        billingService  ??= new Mock<IBillingService>();
        adjustmentRepo  ??= new Mock<IAdjustmentRepository>();
        mpRepo          ??= new Mock<IMaintenancePaymentRepository>();
        db              ??= BuildInMemoryDb();

        var logger = Mock.Of<ILogger<FlatService>>();

        return new FlatService(
            flatRepo.Object,
            billRepo.Object,
            userContext.Object,
            db,
            logger,
            maintConfigRepo.Object,
            billingService.Object,
            adjustmentRepo.Object,
            mpRepo.Object);
    }

    private static AppDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync — success
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidDto_ReturnsMappedDto()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();

        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId))
                   .ReturnsAsync(DefaultSocietyId);

        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync("A101", DefaultSocietyId))
                .ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByEmailAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId))
                .ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByMobileAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId))
                .ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByCodeAsync(It.IsAny<string>()))
                .ReturnsAsync((FlatStatus?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("A101", "Owner A", "9876543210", "owner@test.com", 1500m, null);

        // Act
        var result = await service.CreateAsync(dto, DefaultUserId);

        // Assert
        result.Should().NotBeNull();
        result.FlatNo.Should().Be("A101");
        result.OwnerName.Should().Be("Owner A");
        result.MaintenanceAmount.Should().Be(1500m);
    }

    [Fact]
    public async Task CreateAsync_FlatNoIsNormalisedToUppercase()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync("a101", DefaultSocietyId)).ReturnsAsync((Flat?)null);
        // The normalised flat no passed to the repo should be "A101"
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync("A101", DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("a101", "Owner", null, null, null, null);

        // Act
        var result = await service.CreateAsync(dto, DefaultUserId);

        // Assert — repository receives the normalised (uppercased, trimmed) flat number
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.FlatNo == "A101")), Times.Once);
        result.FlatNo.Should().Be("A101");
    }

    [Fact]
    public async Task CreateAsync_EmailIsNormalisedToLowercase()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByEmailAndSocietyAsync("john@test.com", DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("B202", "John", null, "JOHN@TEST.COM", null, null);

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert — normalised email forwarded to repo
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.ContactEmail == "john@test.com")), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_MobileWithCountryCode91_IsStripped()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByMobileAndSocietyAsync("9876543210", DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("C303", "Owner", "+919876543210", null, null, null);

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert — leading +91 stripped, only 10-digit number stored
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.ContactMobile == "9876543210")), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync — null guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NullDto_ThrowsArgumentNullException()
    {
        // Arrange
        var service = BuildService();

        // Act
        var act = () => service.CreateAsync(null!, DefaultUserId);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync — duplicate detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DuplicateFlatNo_ThrowsDuplicateException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync("A101", DefaultSocietyId))
                .ReturnsAsync(MakeFlat("A101"));

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("A101", "Owner", null, null, null, null);

        // Act
        var act = () => service.CreateAsync(dto, DefaultUserId);

        // Assert
        await act.Should().ThrowAsync<DuplicateException>()
                 .WithMessage("*flat number*");
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ThrowsDuplicateException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByEmailAndSocietyAsync("dup@test.com", DefaultSocietyId))
                .ReturnsAsync(MakeFlat(email: "dup@test.com"));

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("Z999", "Owner", null, "DUP@TEST.COM", null, null);

        // Act
        var act = () => service.CreateAsync(dto, DefaultUserId);

        // Assert
        await act.Should().ThrowAsync<DuplicateException>()
                 .WithMessage("*email*");
    }

    [Fact]
    public async Task CreateAsync_DuplicateMobile_ThrowsDuplicateException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByEmailAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByMobileAndSocietyAsync("9999999999", DefaultSocietyId))
                .ReturnsAsync(MakeFlat(mobile: "9999999999"));

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("Z999", "Owner", "9999999999", null, null, null);

        // Act
        var act = () => service.CreateAsync(dto, DefaultUserId);

        // Assert
        await act.Should().ThrowAsync<DuplicateException>()
                 .WithMessage("*mobile number*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync — invalid status code
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_InvalidStatusCode_ThrowsValidationException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByCodeAsync("bad_code")).ReturnsAsync((FlatStatus?)null);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("D404", "Owner", null, null, null, "bad_code");

        // Act
        var act = () => service.CreateAsync(dto, DefaultUserId);

        // Assert
        await act.Should().ThrowAsync<SocietyLedger.Domain.Exceptions.ValidationException>()
                 .WithMessage("*Invalid flat status code*");
    }

    [Fact]
    public async Task CreateAsync_ValidStatusCode_SetsStatusIdOnEntity()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByCodeAsync("owner_occupied"))
                .ReturnsAsync(new FlatStatus { Id = 1, Code = FlatStatusCodes.OwnerOccupied, DisplayName = "Owner Occupied" });
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("E505", "Owner", null, null, null, "owner_occupied");

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert — entity saved with the resolved status ID
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.StatusId == 1)), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateAsync — MaintenanceAmount defaults to 0 when null
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_NullMaintenanceAmount_DefaultsToZero()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("F606", "Owner", null, null, null, null);

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.MaintenanceAmount == 0m)), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetByPublicIdAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByPublicIdAsync_ExistingFlat_ReturnsMappedDto()
    {
        // Arrange
        var publicId    = Guid.NewGuid();
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        var flat = MakeFlat("G707");
        flat.PublicId = publicId;
        flatRepo.Setup(x => x.GetByPublicIdAsync(publicId, DefaultSocietyId)).ReturnsAsync(flat);

        var service = BuildService(flatRepo, userContext: userContext);

        // Act
        var result = await service.GetByPublicIdAsync(publicId, DefaultUserId);

        // Assert
        result.Should().NotBeNull();
        result!.FlatNo.Should().Be("G707");
        result.PublicId.Should().Be(publicId);
    }

    [Fact]
    public async Task GetByPublicIdAsync_UnknownPublicId_ThrowsNotFoundException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);

        var service = BuildService(flatRepo, userContext: userContext);

        // Act
        var act = () => service.GetByPublicIdAsync(Guid.NewGuid(), DefaultUserId);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // UpdateAsync — success
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ValidDto_ReturnsMappedDto()
    {
        // Arrange
        var publicId    = Guid.NewGuid();
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);

        var existingFlat = MakeFlat("H808");
        existingFlat.PublicId  = publicId;
        existingFlat.SocietyId = DefaultSocietyId;

        flatRepo.Setup(x => x.GetByPublicIdAsync(publicId, DefaultSocietyId)).ReturnsAsync(existingFlat);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.UpdateAsync(It.IsAny<Flat>(), DefaultSocietyId)).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new UpdateFlatDto(publicId, "H808-NEW", "New Owner", null, null, 2000m, null);

        // Act
        var result = await service.UpdateAsync(dto, DefaultUserId);

        // Assert
        result.Should().NotBeNull();
        result!.FlatNo.Should().Be("H808-NEW");
        result.OwnerName.Should().Be("New Owner");
        result.MaintenanceAmount.Should().Be(2000m);
    }

    [Fact]
    public async Task UpdateAsync_NullDto_ThrowsArgumentNullException()
    {
        var service = BuildService();
        var act     = () => service.UpdateAsync(null!, DefaultUserId);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateAsync_FlatNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new UpdateFlatDto(Guid.NewGuid(), null, null, null, null, null, null);

        // Act & Assert
        var act = () => service.UpdateAsync(dto, DefaultUserId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // UpdateAsync — duplicate detection on change
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_DuplicateFlatNoOnChange_ThrowsDuplicateException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);

        var existingFlat  = MakeFlat("I909"); existingFlat.SocietyId = DefaultSocietyId;
        var conflictFlat  = MakeFlat("J010"); // a different flat that already has the target number
        conflictFlat.PublicId = Guid.NewGuid(); // different public id

        flatRepo.Setup(x => x.GetByPublicIdAsync(existingFlat.PublicId, DefaultSocietyId)).ReturnsAsync(existingFlat);
        // When checking if "J010" exists in the society, return the conflict flat
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync("J010", DefaultSocietyId)).ReturnsAsync(conflictFlat);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new UpdateFlatDto(existingFlat.PublicId, "J010", null, null, null, null, null);

        // Act & Assert
        var act = () => service.UpdateAsync(dto, DefaultUserId);
        await act.Should().ThrowAsync<DuplicateException>()
                 .WithMessage("*flat number*");
    }

    [Fact]
    public async Task UpdateAsync_SameFlatNoAsOwn_DoesNotThrowDuplicate()
    {
        // Arrange — a flat being updated to its own existing number should not conflict
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);

        var existingFlat = MakeFlat("K111"); existingFlat.SocietyId = DefaultSocietyId;

        flatRepo.Setup(x => x.GetByPublicIdAsync(existingFlat.PublicId, DefaultSocietyId)).ReturnsAsync(existingFlat);
        // GetByFlatNoAndSocietyAsync returns the same flat (same PublicId) — not a conflict
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync("K111", DefaultSocietyId)).ReturnsAsync(existingFlat);
        flatRepo.Setup(x => x.UpdateAsync(It.IsAny<Flat>(), DefaultSocietyId)).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new UpdateFlatDto(existingFlat.PublicId, "K111", "Updated Owner", null, null, null, null);

        // Act — should not throw
        var act = async () => await service.UpdateAsync(dto, DefaultUserId);
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // UpdateAsync — cannot mark vacant when unpaid bills exist
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_MarkVacantWithUnpaidBills_ThrowsConflictException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var billRepo    = new Mock<IBillRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);

        var existingFlat = MakeFlat("L222");
        existingFlat.SocietyId = DefaultSocietyId;

        var vacantStatus = new FlatStatus { Id = 3, Code = FlatStatusCodes.Vacant, DisplayName = "Vacant" };

        flatRepo.Setup(x => x.GetByPublicIdAsync(existingFlat.PublicId, DefaultSocietyId)).ReturnsAsync(existingFlat);
        flatRepo.Setup(x => x.GetByCodeAsync("vacant")).ReturnsAsync(vacantStatus);
        billRepo.Setup(x => x.HasUnpaidBillsExcludingStatusAsync(existingFlat.Id, DefaultSocietyId,
                    BillStatusCodes.Paid, BillStatusCodes.Cancelled))
                .ReturnsAsync(true);

        var service = BuildService(flatRepo, billRepo, userContext);
        var dto     = new UpdateFlatDto(existingFlat.PublicId, null, null, null, null, null, "vacant");

        // Act & Assert
        var act = () => service.UpdateAsync(dto, DefaultUserId);
        await act.Should().ThrowAsync<ConflictException>()
                 .WithMessage("*unpaid bills*");
    }

    [Fact]
    public async Task UpdateAsync_MarkVacantWithNoBills_Succeeds()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var billRepo    = new Mock<IBillRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);

        var existingFlat = MakeFlat("M333"); existingFlat.SocietyId = DefaultSocietyId;
        var vacantStatus = new FlatStatus { Id = 3, Code = FlatStatusCodes.Vacant, DisplayName = "Vacant" };

        flatRepo.Setup(x => x.GetByPublicIdAsync(existingFlat.PublicId, DefaultSocietyId)).ReturnsAsync(existingFlat);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), It.IsAny<long>())).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.GetByCodeAsync("vacant")).ReturnsAsync(vacantStatus);
        billRepo.Setup(x => x.HasUnpaidBillsExcludingStatusAsync(existingFlat.Id, DefaultSocietyId,
                    BillStatusCodes.Paid, BillStatusCodes.Cancelled))
                .ReturnsAsync(false);
        flatRepo.Setup(x => x.UpdateAsync(It.IsAny<Flat>(), DefaultSocietyId)).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, billRepo, userContext);
        var dto     = new UpdateFlatDto(existingFlat.PublicId, null, null, null, null, null, "vacant");

        // Act & Assert — no exception
        var act = async () => await service.UpdateAsync(dto, DefaultUserId);
        await act.Should().NotThrowAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DeleteByPublicIdAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteByPublicIdAsync_FlatWithNoUnpaidBills_ReturnsTrue()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var billRepo    = new Mock<IBillRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);

        var existing  = MakeFlat("N444"); existing.SocietyId = DefaultSocietyId;
        flatRepo.Setup(x => x.GetByPublicIdAsync(existing.PublicId, DefaultSocietyId)).ReturnsAsync(existing);
        billRepo.Setup(x => x.GetUnpaidBillAmountsAsync(existing.Id, DefaultSocietyId))
                .ReturnsAsync(Array.Empty<(decimal, decimal)>());
        flatRepo.Setup(x => x.DeleteByPublicIdAsync(existing.PublicId, DefaultSocietyId)).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, billRepo, userContext);

        // Act
        var result = await service.DeleteByPublicIdAsync(existing.PublicId, DefaultUserId);

        // Assert
        result.Should().BeTrue();
        flatRepo.Verify(x => x.DeleteByPublicIdAsync(existing.PublicId, DefaultSocietyId), Times.Once);
    }

    [Fact]
    public async Task DeleteByPublicIdAsync_FlatWithUnpaidBills_ThrowsConflictException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var billRepo    = new Mock<IBillRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);

        var existing = MakeFlat("O555"); existing.SocietyId = DefaultSocietyId;
        flatRepo.Setup(x => x.GetByPublicIdAsync(existing.PublicId, DefaultSocietyId)).ReturnsAsync(existing);
        billRepo.Setup(x => x.GetUnpaidBillAmountsAsync(existing.Id, DefaultSocietyId))
                .ReturnsAsync(new[] { (2000m, 0m) });  // one unpaid bill: amount=2000, paidAmount=0

        var service = BuildService(flatRepo, billRepo, userContext);

        // Act & Assert
        var act = () => service.DeleteByPublicIdAsync(existing.PublicId, DefaultUserId);
        await act.Should().ThrowAsync<ConflictException>()
                 .WithMessage("*unpaid bill*");
    }

    [Fact]
    public async Task DeleteByPublicIdAsync_FlatNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);

        var service = BuildService(flatRepo, userContext: userContext);

        // Act & Assert
        var act = () => service.DeleteByPublicIdAsync(Guid.NewGuid(), DefaultUserId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetAllAsync (status list)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsMappedStatusDtos()
    {
        // Arrange
        var flatRepo = new Mock<IFlatRepository>();
        flatRepo.Setup(x => x.GetAllAsync())
                .ReturnsAsync(new List<FlatStatus>
                {
                    new() { Id = 1, Code = FlatStatusCodes.OwnerOccupied,   DisplayName = "Owner Occupied" },
                    new() { Id = 2, Code = FlatStatusCodes.TenantOccupied,  DisplayName = "Tenant" }
                });

        var service = BuildService(flatRepo);

        // Act
        var result = (await service.GetAllAsync()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(s => s.Code == FlatStatusCodes.OwnerOccupied);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Edge cases
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WhitespaceFlatNo_StoresTrimedUppercase()
    {
        // Arrange
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync("P606", DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        // Leading/trailing spaces and lowercase
        var dto = new CreateFlatDto("  p606  ", "Owner", null, null, null, null);

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.FlatNo == "P606")), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhitespaceOnlyOwnerName_StoresNull()
    {
        // An owner name that is all whitespace should be stored as null (NormalizeText returns null for whitespace)
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("Q707", "   ", null, null, null, null);

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert — whitespace-only OwnerName is normalised to null
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.OwnerName == null)), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_NullEmail_SkipsEmailDuplicateCheck()
    {
        // Arrange — no email provided; the duplicate-email repo call should never happen
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("R808", "Owner", null, null, null, null);

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert — repo email check never called
        flatRepo.Verify(x => x.GetByEmailAndSocietyAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NewFlatHasPublicIdSet()
    {
        // Each new flat must have a non-empty PublicId generated by the service
        var flatRepo    = new Mock<IFlatRepository>();
        var userContext = new Mock<IUserContext>();
        userContext.Setup(x => x.GetSocietyIdAsync(DefaultUserId)).ReturnsAsync(DefaultSocietyId);
        flatRepo.Setup(x => x.GetByFlatNoAndSocietyAsync(It.IsAny<string>(), DefaultSocietyId)).ReturnsAsync((Flat?)null);
        flatRepo.Setup(x => x.AddAsync(It.IsAny<Flat>())).Returns(Task.CompletedTask);
        flatRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = BuildService(flatRepo, userContext: userContext);
        var dto     = new CreateFlatDto("S909", "Owner", null, null, null, null);

        // Act
        await service.CreateAsync(dto, DefaultUserId);

        // Assert
        flatRepo.Verify(x => x.AddAsync(It.Is<Flat>(f => f.PublicId != Guid.Empty)), Times.Once);
    }
}
