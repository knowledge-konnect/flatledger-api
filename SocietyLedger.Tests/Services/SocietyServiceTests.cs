using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.Society;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Services;
using Xunit;

namespace SocietyLedger.Tests.Services;

public class SocietyServiceTests
{
    private static Society MakeSociety(long id = 10)
    {
        var s = Society.Create("Test Society", "123 Main St");
        // Use reflection to set private Id for test purposes
        typeof(BaseEntity).GetProperty("Id")!.SetValue(s, id);
        return s;
    }

    private static User AdminUser(long societyId = 10) => new()
    {
        Id = 1, PublicId = Guid.NewGuid(), SocietyId = societyId,
        SocietyPublicId = Guid.NewGuid(), SocietyName = "Test",
        Name = "Admin", Email = "admin@test.com", IsActive = true,
        Role = new Role { Id = 1, Code = RoleCodes.SocietyAdmin, DisplayName = "Admin" }, RoleId = 1,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static User ViewerUser(long societyId = 10) => new()
    {
        Id = 2, PublicId = Guid.NewGuid(), SocietyId = societyId,
        SocietyPublicId = Guid.NewGuid(), SocietyName = "Test",
        Name = "Viewer", Email = "viewer@test.com", IsActive = true,
        Role = new Role { Id = 2, Code = RoleCodes.Viewer, DisplayName = "Viewer" }, RoleId = 2,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static SocietyService Build(
        Mock<ISocietyRepository>? societyRepo = null,
        Mock<IUserRepository>?    userRepo    = null)
    {
        societyRepo ??= new Mock<ISocietyRepository>();
        userRepo    ??= new Mock<IUserRepository>();
        return new SocietyService(societyRepo.Object, userRepo.Object, Mock.Of<ILogger<SocietyService>>());
    }

    // ── GetByUserAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByUserAsync_ValidUser_ReturnsSocietyDto()
    {
        var society     = MakeSociety(10);
        var societyRepo = new Mock<ISocietyRepository>();

        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(x => x.GetByIdAsync(10)).ReturnsAsync(society);

        var service = Build(societyRepo);
        var result  = await service.GetByUserAsync(1);

        result.Should().NotBeNull();
        result.Name.Should().Be("Test Society");
    }

    [Fact]
    public async Task GetByUserAsync_NoSocietyForUser_ThrowsNotFoundException()
    {
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(It.IsAny<long>())).ReturnsAsync((long?)null);

        var service = Build(societyRepo);
        var act     = () => service.GetByUserAsync(99);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetByUserAsync_SocietyRecordMissing_ThrowsNotFoundException()
    {
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(x => x.GetByIdAsync(10)).ReturnsAsync((Society?)null);

        var service = Build(societyRepo);
        var act     = () => service.GetByUserAsync(1);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── GetByPublicIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetByPublicIdAsync_SameCallerSociety_ReturnsSocietyDto()
    {
        var society   = MakeSociety(10);
        var publicId  = society.PublicId;
        var societyRepo = new Mock<ISocietyRepository>();

        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(x => x.GetByPublicIdAsync(publicId)).ReturnsAsync(society);

        var service = Build(societyRepo);
        var result  = await service.GetByPublicIdAsync(publicId, 1);

        result.Should().NotBeNull();
        result.Name.Should().Be("Test Society");
    }

    [Fact]
    public async Task GetByPublicIdAsync_DifferentSociety_ThrowsAuthorizationException()
    {
        var society   = MakeSociety(10);   // belongs to society 10
        var publicId  = society.PublicId;
        var societyRepo = new Mock<ISocietyRepository>();

        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(99L); // caller is in society 99
        societyRepo.Setup(x => x.GetByPublicIdAsync(publicId)).ReturnsAsync(society);

        var service = Build(societyRepo);
        var act     = () => service.GetByPublicIdAsync(publicId, 1);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task GetByPublicIdAsync_SocietyNotFound_ThrowsNotFoundException()
    {
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>())).ReturnsAsync((Society?)null);

        var service = Build(societyRepo);
        var act     = () => service.GetByPublicIdAsync(Guid.NewGuid(), 1);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_AdminUser_UpdatesAndReturnsDto()
    {
        var society     = MakeSociety(10);
        var admin       = AdminUser(10);
        var publicId    = society.PublicId;
        var societyRepo = new Mock<ISocietyRepository>();
        var userRepo    = new Mock<IUserRepository>();

        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(admin.Id)).ReturnsAsync(10L);
        societyRepo.Setup(x => x.GetByPublicIdAsync(publicId)).ReturnsAsync(society);
        societyRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        userRepo.Setup(x => x.GetByIdAsync(admin.Id)).ReturnsAsync(admin);

        var service = Build(societyRepo, userRepo);
        var request = new UpdateSocietyRequest { Name = "New Name" };
        var result  = await service.UpdateAsync(publicId, request, admin.Id);

        result.Name.Should().Be("New Name");
        societyRepo.Verify(x => x.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ViewerUser_ThrowsAuthorizationException()
    {
        var society     = MakeSociety(10);
        var viewer      = ViewerUser(10);
        var publicId    = society.PublicId;
        var societyRepo = new Mock<ISocietyRepository>();
        var userRepo    = new Mock<IUserRepository>();

        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(viewer.Id)).ReturnsAsync(10L);
        societyRepo.Setup(x => x.GetByPublicIdAsync(publicId)).ReturnsAsync(society);
        userRepo.Setup(x => x.GetByIdAsync(viewer.Id)).ReturnsAsync(viewer);

        var service = Build(societyRepo, userRepo);
        var act     = () => service.UpdateAsync(publicId, new UpdateSocietyRequest { Name = "Hack" }, viewer.Id);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task UpdateAsync_CrossSocietyAccess_ThrowsAuthorizationException()
    {
        var society     = MakeSociety(10); // society 10
        var admin       = AdminUser(99);    // caller is in society 99
        var publicId    = society.PublicId;
        var societyRepo = new Mock<ISocietyRepository>();
        var userRepo    = new Mock<IUserRepository>();

        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(admin.Id)).ReturnsAsync(99L);
        societyRepo.Setup(x => x.GetByPublicIdAsync(publicId)).ReturnsAsync(society);
        userRepo.Setup(x => x.GetByIdAsync(admin.Id)).ReturnsAsync(admin);

        var service = Build(societyRepo, userRepo);
        var act     = () => service.UpdateAsync(publicId, new UpdateSocietyRequest { Name = "X" }, admin.Id);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task UpdateAsync_SocietyNotFound_ThrowsNotFoundException()
    {
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(x => x.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(x => x.GetByPublicIdAsync(It.IsAny<Guid>())).ReturnsAsync((Society?)null);

        var service = Build(societyRepo);
        var act     = () => service.UpdateAsync(Guid.NewGuid(), new UpdateSocietyRequest { Name = "X" }, 1);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
