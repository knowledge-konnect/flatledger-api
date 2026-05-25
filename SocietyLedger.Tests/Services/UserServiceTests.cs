using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.Auth;
using SocietyLedger.Application.DTOs.User;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services;
using Xunit;

namespace SocietyLedger.Tests.Services;

public class UserServiceTests
{
    private static AppDbContext InMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(opts);
    }

    private static Role AdminRole() => new() { Id = 1, Code = RoleCodes.SocietyAdmin, DisplayName = "Society Admin" };
    private static Role ViewerRole() => new() { Id = 2, Code = RoleCodes.Viewer, DisplayName = "Viewer" };

    private static User ActiveAdmin(long societyId = 10) => new()
    {
        Id = 1, PublicId = Guid.NewGuid(), SocietyId = societyId,
        SocietyPublicId = Guid.NewGuid(), SocietyName = "Test",
        Name = "Admin", Email = "admin@test.com",
        IsActive = true, Role = AdminRole(), RoleId = 1,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static User ActiveViewer(long societyId = 10) => new()
    {
        Id = 2, PublicId = Guid.NewGuid(), SocietyId = societyId,
        SocietyPublicId = Guid.NewGuid(), SocietyName = "Test",
        Name = "Viewer", Email = "viewer@test.com",
        IsActive = true, Role = ViewerRole(), RoleId = 2,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private static UserService Build(
        Mock<IUserRepository>? userRepo = null,
        Mock<IRoleRepository>? roleRepo = null,
        Mock<PasswordHasher>?  hasher   = null,
        AppDbContext?           db       = null)
    {
        userRepo ??= new Mock<IUserRepository>();
        roleRepo ??= new Mock<IRoleRepository>();
        hasher   ??= new Mock<PasswordHasher>();
        db       ??= InMemoryDb();

        return new UserService(userRepo.Object, roleRepo.Object,
            hasher.Object, Mock.Of<ILogger<UserService>>(), db);
    }

    // ── GetUserByIdAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserByIdAsync_ActiveUser_ReturnsDto()
    {
        var user     = ActiveAdmin();
        var userRepo = new Mock<IUserRepository>();
        var roleRepo = new Mock<IRoleRepository>();

        userRepo.Setup(x => x.GetByIdAsync(user.Id)).ReturnsAsync(user);
        roleRepo.Setup(x => x.GetByIdAsync(user.RoleId)).ReturnsAsync(AdminRole());

        var service = Build(userRepo, roleRepo);
        var result  = await service.GetUserByIdAsync(user.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Admin");
        result.Role.Should().Be(RoleCodes.SocietyAdmin);
    }

    [Fact]
    public async Task GetUserByIdAsync_InactiveUser_ReturnsNull()
    {
        var user = ActiveAdmin(); user.IsActive = false;
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var service = Build(userRepo);
        var result  = await service.GetUserByIdAsync(user.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByIdAsync_UserNotFound_ReturnsNull()
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((User?)null);

        var service = Build(userRepo);
        var result  = await service.GetUserByIdAsync(999);

        result.Should().BeNull();
    }

    // ── UpdateProfileAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfileAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = Build();
        var act     = () => service.UpdateProfileAsync(1, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateProfileAsync_UserNotFound_ThrowsNotFoundException()
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(It.IsAny<long>())).ReturnsAsync((User?)null);

        var service = Build(userRepo);
        var act     = () => service.UpdateProfileAsync(99, new UpdateProfileRequest { Mobile = "9876543210" });
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateProfileAsync_ValidMobile_UpdatesAndReturnsProfile()
    {
        var user     = ActiveAdmin(); user.Mobile = "1111111111";
        var userRepo = new Mock<IUserRepository>();
        var roleRepo = new Mock<IRoleRepository>();

        userRepo.Setup(x => x.GetByIdAsync(user.Id)).ReturnsAsync(user);
        userRepo.Setup(x => x.GetByMobileAndSocietyAsync("9876543210", user.SocietyId)).ReturnsAsync((User?)null);
        userRepo.Setup(x => x.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
        userRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);
        roleRepo.Setup(x => x.GetByIdAsync(user.RoleId)).ReturnsAsync(AdminRole());

        var service = Build(userRepo, roleRepo);
        var result  = await service.UpdateProfileAsync(user.Id, new UpdateProfileRequest { Mobile = "9876543210" });

        result.Should().NotBeNull();
        result.Mobile.Should().Be("9876543210");
        userRepo.Verify(x => x.UpdateAsync(It.Is<User>(u => u.Mobile == "9876543210")), Times.Once);
    }

    [Fact]
    public async Task UpdateProfileAsync_DuplicateMobile_ThrowsDuplicateException()
    {
        var user      = ActiveAdmin(); user.Mobile = "1111111111";
        var otherUser = ActiveViewer(); otherUser.Mobile = "9876543210";
        var userRepo  = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByIdAsync(user.Id)).ReturnsAsync(user);
        userRepo.Setup(x => x.GetByMobileAndSocietyAsync("9876543210", user.SocietyId)).ReturnsAsync(otherUser);

        var service = Build(userRepo);
        var act     = () => service.UpdateProfileAsync(user.Id, new UpdateProfileRequest { Mobile = "9876543210" });

        await act.Should().ThrowAsync<DuplicateException>().WithMessage("*mobile number*");
    }

    [Fact]
    public async Task UpdateProfileAsync_WhitespaceOnlyMobile_ThrowsValidationException()
    {
        var user = ActiveAdmin();
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(user.Id)).ReturnsAsync(user);

        var service = Build(userRepo);
        var act     = () => service.UpdateProfileAsync(user.Id, new UpdateProfileRequest { Mobile = "   " });

        await act.Should().ThrowAsync<SocietyLedger.Domain.Exceptions.ValidationException>()
                 .WithMessage("*10-digit*");
    }

    // ── GetUsersForAdminAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetUsersForAdminAsync_AdminUser_ReturnsList()
    {
        var admin    = ActiveAdmin();
        var userRepo = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByIdAsync(admin.Id)).ReturnsAsync(admin);
        userRepo.Setup(x => x.GetBySocietyIdAsync(admin.SocietyId))
                .ReturnsAsync(new[] { admin });

        var service = Build(userRepo);
        var result  = await service.GetUsersForAdminAsync(admin.Id);

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetUsersForAdminAsync_ViewerCaller_ThrowsAuthorizationException()
    {
        var viewer   = ActiveViewer();
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(viewer.Id)).ReturnsAsync(viewer);

        var service = Build(userRepo);
        var act     = () => service.GetUsersForAdminAsync(viewer.Id);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    // ── DeleteUserForAdminAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUserForAdminAsync_NonAdminCaller_ThrowsAuthorizationException()
    {
        var viewer   = ActiveViewer();
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(viewer.Id)).ReturnsAsync(viewer);

        var service = Build(userRepo);
        var act     = () => service.DeleteUserForAdminAsync(Guid.NewGuid(), viewer.Id);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task DeleteUserForAdminAsync_AdminDeletesOtherUser_CallsDeleteAndReturnsTrue()
    {
        var admin    = ActiveAdmin();
        var target   = ActiveViewer(); target.PublicId = Guid.NewGuid();
        var userRepo = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByIdAsync(admin.Id)).ReturnsAsync(admin);
        userRepo.Setup(x => x.GetByPublicIdAsync(target.PublicId, admin.SocietyId)).ReturnsAsync(target);
        userRepo.Setup(x => x.UpdateAsync(It.IsAny<User>())).Returns(Task.CompletedTask);
        userRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = Build(userRepo);
        var result  = await service.DeleteUserForAdminAsync(target.PublicId, admin.Id);

        result.Should().BeTrue();
    }

    // ── CreateUserForAdminAsync ────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserForAdminAsync_NonAdmin_ThrowsAuthorizationException()
    {
        var viewer   = ActiveViewer();
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(viewer.Id)).ReturnsAsync(viewer);

        var service = Build(userRepo);
        var act     = () => service.CreateUserForAdminAsync(
                         new CreateUserDto("Name", "e@t.com", null, null, null, "pass"), viewer.Id);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    // ── UpdateUserForAdminAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateUserForAdminAsync_NonAdmin_ThrowsAuthorizationException()
    {
        var viewer   = ActiveViewer();
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByIdAsync(viewer.Id)).ReturnsAsync(viewer);

        var service = Build(userRepo);
        var act     = () => service.UpdateUserForAdminAsync(
                         new UpdateUserDto(Guid.NewGuid(), "N", "e@t.com", null, RoleCodes.Viewer), viewer.Id);

        await act.Should().ThrowAsync<AuthorizationException>();
    }
}
