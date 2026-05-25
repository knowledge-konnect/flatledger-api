using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.MaintenanceConfig;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Services;
using Xunit;

namespace SocietyLedger.Tests.Services;

public class MaintenanceConfigServiceTests
{
    private static readonly Guid SocietyPublicId = Guid.NewGuid();
    private const long SocietyId = 10L;
    private const long AdminUserId = 1L;
    private const long ViewerUserId = 2L;

    private static Society MakeSociety() => Society.Create("Test Society");

    private static User AdminUser() => new()
    {
        Id = AdminUserId, SocietyId = SocietyId,
        SocietyPublicId = SocietyPublicId,
        Name = "Admin", Email = "admin@test.com", IsActive = true,
        Role = new Role { Id = 1, Code = RoleCodes.SocietyAdmin, DisplayName = "Admin" }, RoleId = 1,
        SocietyName = "Test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static User ViewerUser() => new()
    {
        Id = ViewerUserId, SocietyId = SocietyId,
        SocietyPublicId = SocietyPublicId,
        Name = "Viewer", Email = "viewer@test.com", IsActive = true,
        Role = new Role { Id = 2, Code = RoleCodes.Viewer, DisplayName = "Viewer" }, RoleId = 2,
        SocietyName = "Test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static MaintenanceConfigService Build(
        Mock<IMaintenanceConfigRepository>? configRepo  = null,
        Mock<ISocietyRepository>?           societyRepo = null,
        Mock<IUserRepository>?              userRepo    = null)
    {
        configRepo  ??= new Mock<IMaintenanceConfigRepository>();
        societyRepo ??= new Mock<ISocietyRepository>();
        userRepo    ??= new Mock<IUserRepository>();
        return new MaintenanceConfigService(
            configRepo.Object, societyRepo.Object,
            userRepo.Object, Mock.Of<ILogger<MaintenanceConfigService>>());
    }

    // ── GetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ExistingConfig_ReturnsConfig()
    {
        var society     = MakeSociety();
        var configRepo  = new Mock<IMaintenanceConfigRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userRepo    = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(AdminUser());
        societyRepo.Setup(x => x.GetByPublicIdAsync(SocietyPublicId)).ReturnsAsync(society);
        configRepo.Setup(x => x.GetBySocietyIdAsync(It.IsAny<long>()))
                  .ReturnsAsync(new MaintenanceConfigResponse
                  {
                      SocietyPublicId      = SocietyPublicId,
                      DefaultMonthlyCharge = 1500m,
                      DueDayOfMonth        = 10,
                      LateFeePerMonth      = 50m,
                      GracePeriodDays      = 5
                  });

        var service = Build(configRepo, societyRepo, userRepo);
        var result  = await service.GetAsync(SocietyPublicId, AdminUserId);

        result.Should().NotBeNull();
        result.DefaultMonthlyCharge.Should().Be(1500m);
        result.DueDayOfMonth.Should().Be(10);
    }

    [Fact]
    public async Task GetAsync_NoConfigExists_ReturnsDefaults()
    {
        // If no config has been saved yet service returns safe defaults (all zeros)
        var society     = MakeSociety();
        var configRepo  = new Mock<IMaintenanceConfigRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userRepo    = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(AdminUser());
        societyRepo.Setup(x => x.GetByPublicIdAsync(SocietyPublicId)).ReturnsAsync(society);
        configRepo.Setup(x => x.GetBySocietyIdAsync(It.IsAny<long>()))
                  .ReturnsAsync((MaintenanceConfigResponse?)null);

        var service = Build(configRepo, societyRepo, userRepo);
        var result  = await service.GetAsync(SocietyPublicId, AdminUserId);

        result.DefaultMonthlyCharge.Should().Be(0);
        result.DueDayOfMonth.Should().Be(1);
        result.GracePeriodDays.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_InactiveUser_ThrowsAuthenticationException()
    {
        var inactiveUser = AdminUser(); inactiveUser.IsActive = false;
        var userRepo     = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(inactiveUser);

        var service = Build(userRepo: userRepo);
        var act     = () => service.GetAsync(SocietyPublicId, AdminUserId);

        await act.Should().ThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task GetAsync_UserFromDifferentSociety_ThrowsAuthorizationException()
    {
        // User belongs to a different society
        var user = AdminUser(); user.SocietyPublicId = Guid.NewGuid();
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(user);

        var service = Build(userRepo: userRepo);
        var act     = () => service.GetAsync(SocietyPublicId, AdminUserId);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task GetAsync_SocietyNotFound_ThrowsNotFoundException()
    {
        var userRepo    = new Mock<IUserRepository>();
        var societyRepo = new Mock<ISocietyRepository>();

        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(AdminUser());
        societyRepo.Setup(x => x.GetByPublicIdAsync(SocietyPublicId)).ReturnsAsync((Society?)null);

        var service = Build(societyRepo: societyRepo, userRepo: userRepo);
        var act     = () => service.GetAsync(SocietyPublicId, AdminUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── SaveAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_AdminUser_SavesAndReturnsConfig()
    {
        var society     = MakeSociety();
        var configRepo  = new Mock<IMaintenanceConfigRepository>();
        var societyRepo = new Mock<ISocietyRepository>();
        var userRepo    = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(AdminUser());
        societyRepo.Setup(x => x.GetByPublicIdAsync(SocietyPublicId)).ReturnsAsync(society);
        configRepo.Setup(x => x.UpsertAsync(
                    It.IsAny<long>(), SocietyPublicId,
                    It.IsAny<SaveMaintenanceConfigRequest>(), AdminUserId))
                  .Returns(Task.CompletedTask);

        var service = Build(configRepo, societyRepo, userRepo);
        var request = new SaveMaintenanceConfigRequest
        {
            DefaultMonthlyCharge = 2000m,
            DueDayOfMonth        = 15,
            LateFeePerMonth      = 100m,
            GracePeriodDays      = 7
        };

        var result = await service.SaveAsync(SocietyPublicId, request, AdminUserId);

        result.DefaultMonthlyCharge.Should().Be(2000m);
        result.DueDayOfMonth.Should().Be(15);
        configRepo.Verify(x => x.UpsertAsync(
            It.IsAny<long>(), SocietyPublicId,
            It.IsAny<SaveMaintenanceConfigRequest>(), AdminUserId), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ViewerUser_ThrowsAuthorizationException()
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(ViewerUserId)).ReturnsAsync(ViewerUser());

        var service = Build(userRepo: userRepo);
        var act     = () => service.SaveAsync(SocietyPublicId, new SaveMaintenanceConfigRequest(), ViewerUserId);

        await act.Should().ThrowAsync<AuthorizationException>()
                 .WithMessage("*Only Society Admin*");
    }

    [Fact]
    public async Task SaveAsync_SocietyNotFound_ThrowsNotFoundException()
    {
        var userRepo    = new Mock<IUserRepository>();
        var societyRepo = new Mock<ISocietyRepository>();

        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(AdminUser());
        societyRepo.Setup(x => x.GetByPublicIdAsync(SocietyPublicId)).ReturnsAsync((Society?)null);

        var service = Build(societyRepo: societyRepo, userRepo: userRepo);
        var act     = () => service.SaveAsync(SocietyPublicId, new SaveMaintenanceConfigRequest(), AdminUserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SaveAsync_CrossSocietyUser_ThrowsAuthorizationException()
    {
        var user = AdminUser(); user.SocietyPublicId = Guid.NewGuid();
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(AdminUserId)).ReturnsAsync(user);

        var service = Build(userRepo: userRepo);
        var act     = () => service.SaveAsync(SocietyPublicId, new SaveMaintenanceConfigRequest(), AdminUserId);

        await act.Should().ThrowAsync<AuthorizationException>();
    }
}
