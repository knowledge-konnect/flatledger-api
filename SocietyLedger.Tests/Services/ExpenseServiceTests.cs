using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.Expense;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Services;
using SocietyLedger.Infrastructure.Services.Common;
using Xunit;

namespace SocietyLedger.Tests.Services;

/// <summary>
/// Unit tests for ExpenseService: create, get, update, delete, near-duplicate guard.
/// </summary>
public class ExpenseServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private const long UserId    = 1L;
    private const long SocietyId = 10L;
    private static readonly Guid SocietyPublicId = Guid.NewGuid();

    private static ExpenseService Build(
        Mock<IExpenseRepository>?   expRepo         = null,
        Mock<ISocietyRepository>?   societyRepo     = null,
        Mock<IUserContext>?         userContext     = null,
        Mock<IDashboardService>?    dashboardService = null)
    {
        expRepo          ??= new Mock<IExpenseRepository>();
        societyRepo      ??= new Mock<ISocietyRepository>();
        userContext       ??= new Mock<IUserContext>();
        dashboardService ??= new Mock<IDashboardService>();

        return new ExpenseService(
            expRepo.Object,
            societyRepo.Object,
            userContext.Object,
            dashboardService.Object,
            Mock.Of<ILogger<ExpenseService>>());
    }

    private static ExpenseEntity MakeEntity(
        Guid? publicId = null, decimal amount = 1500m,
        DateOnly? date = null, string categoryCode = "electricity", string? vendor = null)
        => new()
        {
            PublicId        = publicId ?? Guid.NewGuid(),
            SocietyId       = SocietyId,
            SocietyPublicId = SocietyPublicId,
            DateIncurred    = date ?? DateOnly.FromDateTime(DateTime.UtcNow),
            CategoryCode    = categoryCode,
            Amount          = amount,
            Vendor          = vendor,
            CreatedAt       = DateTime.UtcNow
        };

    // ──────────────────────────────────────────────────────────────────────────
    // CreateExpenseAsync — success
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateExpenseAsync_ValidRequest_ReturnsResponse()
    {
        // Arrange
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId))
                   .ReturnsAsync(new DateOnly(2024, 1, 1));
        expRepo.Setup(x => x.IsDuplicateRecentAsync(
                    SocietyId, It.IsAny<DateOnly>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
               .ReturnsAsync(false);
        expRepo.Setup(x => x.AddAsync(It.IsAny<object>())).Returns(Task.CompletedTask);
        expRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var createdEntity = MakeEntity(amount: 2000m);
        expRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), SocietyId))
               .ReturnsAsync(createdEntity);

        var service = Build(expRepo, societyRepo, userCtx);
        var request = new CreateExpenseRequest
        {
            Date         = DateOnly.FromDateTime(DateTime.UtcNow),
            Amount       = 2000m,
            CategoryCode = "electricity",
            Vendor       = "MSEB"
        };

        // Act
        var result = await service.CreateExpenseAsync(UserId, request);

        // Assert
        result.Should().NotBeNull();
        result.Amount.Should().Be(2000m);
        expRepo.Verify(x => x.AddAsync(It.IsAny<object>()), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateExpenseAsync — date before onboarding
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateExpenseAsync_DateBeforeOnboarding_ThrowsValidationException()
    {
        // Arrange
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        // Society onboarded on 2024-06-01
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId))
                   .ReturnsAsync(new DateOnly(2024, 6, 1));

        var service = Build(expRepo, societyRepo, userCtx);
        var request = new CreateExpenseRequest
        {
            Date         = new DateOnly(2024, 1, 1),  // before onboarding
            Amount       = 500m,
            CategoryCode = "electricity"
        };

        // Act & Assert
        var act = () => service.CreateExpenseAsync(UserId, request);
        await act.Should().ThrowAsync<SocietyLedger.Domain.Exceptions.ValidationException>()
                 .WithMessage("*onboarding date*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateExpenseAsync — near-duplicate detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateExpenseAsync_NearDuplicateWithin5Minutes_ThrowsConflictException()
    {
        // Arrange
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        // Simulate that a duplicate was found within the last 5 minutes
        expRepo.Setup(x => x.IsDuplicateRecentAsync(
                    SocietyId, It.IsAny<DateOnly>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
               .ReturnsAsync(true);

        var service = Build(expRepo, societyRepo, userCtx);
        var request = new CreateExpenseRequest
        {
            Date         = DateOnly.FromDateTime(DateTime.UtcNow),
            Amount       = 500m,
            CategoryCode = "electricity",
            Vendor       = "MSEB"
        };

        // Act & Assert
        var act = () => service.CreateExpenseAsync(UserId, request);
        await act.Should().ThrowAsync<ConflictException>()
                 .WithMessage("*similar expense*");
    }

    [Fact]
    public async Task CreateExpenseAsync_NullVendor_TreatedAsEmptyStringForDuplicateCheck()
    {
        // Arrange — vendor is null; service should substitute empty string before calling repo
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        expRepo.Setup(x => x.IsDuplicateRecentAsync(
                    SocietyId, It.IsAny<DateOnly>(), It.IsAny<decimal>(),
                    It.IsAny<string>(), string.Empty, It.IsAny<DateTime>()))
               .ReturnsAsync(false);
        expRepo.Setup(x => x.AddAsync(It.IsAny<object>())).Returns(Task.CompletedTask);
        expRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        expRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), SocietyId))
               .ReturnsAsync(MakeEntity());

        var service = Build(expRepo, societyRepo, userCtx);
        var request = new CreateExpenseRequest
        {
            Date         = DateOnly.FromDateTime(DateTime.UtcNow),
            Amount       = 500m,
            CategoryCode = "electricity",
            Vendor       = null
        };

        // Act — should not throw
        var act = async () => await service.CreateExpenseAsync(UserId, request);
        await act.Should().NotThrowAsync();

        // Assert — duplicate check was called with empty string for vendor
        expRepo.Verify(x => x.IsDuplicateRecentAsync(
            SocietyId, It.IsAny<DateOnly>(), It.IsAny<decimal>(),
            It.IsAny<string>(), string.Empty, It.IsAny<DateTime>()), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateExpenseAsync — society not found
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateExpenseAsync_SocietyNotFound_ThrowsNotFoundException()
    {
        // Arrange — onboarding date returns null (society doesn't exist)
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync((DateOnly?)null);

        var service = Build(expRepo, societyRepo, userCtx);
        var request = new CreateExpenseRequest
        {
            Date         = DateOnly.FromDateTime(DateTime.UtcNow),
            Amount       = 500m,
            CategoryCode = "electricity"
        };

        // Act & Assert
        var act = () => service.CreateExpenseAsync(UserId, request);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetExpenseAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExpenseAsync_ExistingId_ReturnsResponse()
    {
        var expRepo = new Mock<IExpenseRepository>();
        var userCtx = new Mock<IUserContext>();
        var publicId = Guid.NewGuid();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(publicId, SocietyId))
               .ReturnsAsync(MakeEntity(publicId, amount: 750m));

        var service = Build(expRepo, userContext: userCtx);

        var result = await service.GetExpenseAsync(publicId, UserId);

        result.Should().NotBeNull();
        result.Amount.Should().Be(750m);
        result.PublicId.Should().Be(publicId);
    }

    [Fact]
    public async Task GetExpenseAsync_UnknownId_ThrowsNotFoundException()
    {
        var expRepo = new Mock<IExpenseRepository>();
        var userCtx = new Mock<IUserContext>();
        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), SocietyId)).ReturnsAsync((ExpenseEntity?)null);

        var service = Build(expRepo, userContext: userCtx);

        var act = () => service.GetExpenseAsync(Guid.NewGuid(), UserId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // UpdateExpenseAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateExpenseAsync_ValidRequest_ReturnsUpdatedResponse()
    {
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();
        var publicId    = Guid.NewGuid();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(publicId, SocietyId)).ReturnsAsync(MakeEntity(publicId));
        expRepo.Setup(x => x.UpdateFieldsAsync(publicId, SocietyId, It.IsAny<UpdateExpenseRequest>()))
               .ReturnsAsync(MakeEntity(publicId, amount: 3000m));

        var service = Build(expRepo, societyRepo, userCtx);
        var request = new UpdateExpenseRequest { Amount = 3000m };

        var result = await service.UpdateExpenseAsync(publicId, UserId, request);

        result.Amount.Should().Be(3000m);
    }

    [Fact]
    public async Task UpdateExpenseAsync_ExpenseNotFound_ThrowsNotFoundException()
    {
        var expRepo = new Mock<IExpenseRepository>();
        var userCtx = new Mock<IUserContext>();
        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), SocietyId)).ReturnsAsync((ExpenseEntity?)null);

        var service = Build(expRepo, userContext: userCtx);

        var act = () => service.UpdateExpenseAsync(Guid.NewGuid(), UserId, new UpdateExpenseRequest());
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateExpenseAsync_DateBeforeOnboarding_ThrowsValidationException()
    {
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();
        var publicId    = Guid.NewGuid();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(publicId, SocietyId)).ReturnsAsync(MakeEntity(publicId));
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 6, 1));

        var service = Build(expRepo, societyRepo, userCtx);
        var request = new UpdateExpenseRequest { Date = new DateOnly(2023, 1, 1) }; // before onboarding

        var act = () => service.UpdateExpenseAsync(publicId, UserId, request);
        await act.Should().ThrowAsync<SocietyLedger.Domain.Exceptions.ValidationException>()
                 .WithMessage("*onboarding date*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DeleteExpenseAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteExpenseAsync_ExistingId_CallsDeleteAndSave()
    {
        var expRepo  = new Mock<IExpenseRepository>();
        var userCtx  = new Mock<IUserContext>();
        var publicId = Guid.NewGuid();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(publicId, SocietyId)).ReturnsAsync(MakeEntity(publicId));
        expRepo.Setup(x => x.DeleteByPublicIdAsync(publicId, SocietyId)).Returns(Task.CompletedTask);
        expRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = Build(expRepo, userContext: userCtx);

        await service.DeleteExpenseAsync(publicId, UserId);

        expRepo.Verify(x => x.DeleteByPublicIdAsync(publicId, SocietyId), Times.Once);
        expRepo.Verify(x => x.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteExpenseAsync_UnknownId_ThrowsNotFoundException()
    {
        var expRepo = new Mock<IExpenseRepository>();
        var userCtx = new Mock<IUserContext>();
        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), SocietyId)).ReturnsAsync((ExpenseEntity?)null);

        var service = Build(expRepo, userContext: userCtx);

        var act = () => service.DeleteExpenseAsync(Guid.NewGuid(), UserId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetExpensesBySocietyAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetExpensesBySocietyAsync_ReturnsAllExpenses()
    {
        var expRepo = new Mock<IExpenseRepository>();
        var userCtx = new Mock<IUserContext>();
        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetBySocietyIdAsync(SocietyId))
               .ReturnsAsync(new[] { MakeEntity(amount: 100m), MakeEntity(amount: 200m) });

        var service = Build(expRepo, userContext: userCtx);

        var result = (await service.GetExpensesBySocietyAsync(UserId)).ToList();

        result.Should().HaveCount(2);
        result.Sum(e => e.Amount).Should().Be(300m);
    }

    [Fact]
    public async Task GetExpensesBySocietyAsync_NoExpenses_ReturnsEmptyList()
    {
        var expRepo = new Mock<IExpenseRepository>();
        var userCtx = new Mock<IUserContext>();
        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetBySocietyIdAsync(SocietyId))
               .ReturnsAsync(Array.Empty<ExpenseEntity>());

        var service = Build(expRepo, userContext: userCtx);

        var result = await service.GetExpensesBySocietyAsync(UserId);

        result.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dashboard cache invalidation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteExpenseAsync_AfterSuccess_InvalidatesDashboardCache()
    {
        var expRepo   = new Mock<IExpenseRepository>();
        var userCtx   = new Mock<IUserContext>();
        var dashboard = new Mock<IDashboardService>();
        var publicId  = Guid.NewGuid();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        expRepo.Setup(x => x.GetByPublicIdAsync(publicId, SocietyId)).ReturnsAsync(MakeEntity(publicId));
        expRepo.Setup(x => x.DeleteByPublicIdAsync(publicId, SocietyId)).Returns(Task.CompletedTask);
        expRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        dashboard.Setup(x => x.InvalidateDashboardCache(SocietyId));

        var service = Build(expRepo, userContext: userCtx, dashboardService: dashboard);

        await service.DeleteExpenseAsync(publicId, UserId);

        dashboard.Verify(x => x.InvalidateDashboardCache(SocietyId), Times.Once);
    }

    [Fact]
    public async Task CreateExpenseAsync_AfterSuccess_InvalidatesDashboardCache()
    {
        var expRepo     = new Mock<IExpenseRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userCtx     = new Mock<IUserContext>();
        var dashboard   = new Mock<IDashboardService>();

        userCtx.Setup(x => x.GetSocietyIdAsync(UserId)).ReturnsAsync(SocietyId);
        societyRepo.Setup(x => x.GetOnboardingDateAsync(SocietyId)).ReturnsAsync(new DateOnly(2024, 1, 1));
        expRepo.Setup(x => x.IsDuplicateRecentAsync(
                   SocietyId, It.IsAny<DateOnly>(), It.IsAny<decimal>(),
                   It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(false);
        expRepo.Setup(x => x.AddAsync(It.IsAny<object>())).Returns(Task.CompletedTask);
        expRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        expRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>(), SocietyId)).ReturnsAsync(MakeEntity());

        var service = Build(expRepo, societyRepo, userCtx, dashboard);
        var request = new CreateExpenseRequest
        {
            Date = DateOnly.FromDateTime(DateTime.UtcNow), Amount = 100m, CategoryCode = "electricity"
        };

        await service.CreateExpenseAsync(UserId, request);

        dashboard.Verify(x => x.InvalidateDashboardCache(SocietyId), Times.Once);
    }
}
