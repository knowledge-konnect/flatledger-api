using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using SocietyLedger.Application.DTOs.Auth;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services;
using Xunit;

namespace SocietyLedger.Tests.Services;

public class AuthServiceTests
{
    private const string IP = "127.0.0.1";

    private static Role AdminRole() => new() { Id = 1, Code = RoleCodes.SocietyAdmin, DisplayName = "Society Admin" };

    private static User ActiveUser(string? hash = "hash") => new()
    {
        Id = 1,
        PublicId = Guid.NewGuid(),
        SocietyId = 10,
        SocietyPublicId = Guid.NewGuid(),
        SocietyName = "Test Society",
        Name = "Alice",
        Email = "alice@test.com",
        PasswordHash = hash,
        IsActive = true,
        Role = AdminRole(),
        RoleId = 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static AppDbContext InMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(opts);
    }

    private static AuthService Build(
        Mock<IUserRepository>? userRepo = null,
        Mock<IRoleRepository>? roleRepo = null,
        Mock<ISocietyRepository>? societyRepo = null,
        Mock<ITokenService>? tokenService = null,
        Mock<ISubscriptionService>? subService = null,
        Mock<IEmailService>? emailService = null,
        PasswordHasher? hasher = null,
        Mock<IRefreshTokenRepository>? refreshRepo = null,
        AppDbContext? db = null,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null)
    {
        userRepo ??= new Mock<IUserRepository>();
        roleRepo ??= new Mock<IRoleRepository>();
        societyRepo ??= new Mock<ISocietyRepository>();
        tokenService ??= new Mock<ITokenService>();
        subService ??= new Mock<ISubscriptionService>();
        emailService ??= new Mock<IEmailService>();
        hasher ??= new PasswordHasher();
        refreshRepo ??= new Mock<IRefreshTokenRepository>();
        db ??= InMemoryDb();
        configuration ??= new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:FrontendUrl"] = "http://localhost:5173"
            })
            .Build();
        environment ??= Mock.Of<IHostEnvironment>(e =>
            e.EnvironmentName == Environments.Development);

        return new AuthService(
            userRepo.Object, roleRepo.Object, societyRepo.Object,
            tokenService.Object, subService.Object, emailService.Object,
            hasher, Mock.Of<ILogger<AuthService>>(), db, refreshRepo.Object,
            configuration, environment);
    }

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsLoginResponse()
    {
        var hasher = new PasswordHasher();
        var user = ActiveUser(hasher.Hash("pass123"));
        var userRepo = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var refreshRepo = new Mock<IRefreshTokenRepository>();

        userRepo.Setup(x => x.GetByUsernameOrEmailAsync("alice@test.com")).ReturnsAsync(user);
        tokenService.Setup(x => x.GenerateAccessToken(It.IsAny<TokenClaims>(), out It.Ref<DateTime>.IsAny))
                    .Returns("access-token");
        tokenService.Setup(x => x.GenerateRefreshToken())
                    .Returns(new RefreshTokenPair("refresh-token", DateTime.UtcNow.AddDays(7)));
        tokenService.Setup(x => x.HashToken(It.IsAny<string>())).Returns("hashed");
        userRepo.Setup(x => x.UpdateLastLoginAsync(user.Id, It.IsAny<DateTime>())).Returns(Task.CompletedTask);
        refreshRepo.Setup(x => x.AddAsync(It.IsAny<RefreshTokenEntity>())).Returns(Task.CompletedTask);
        refreshRepo.Setup(x => x.SaveChangesAsync()).Returns(Task.CompletedTask);

        var service = Build(userRepo, tokenService: tokenService, hasher: hasher, refreshRepo: refreshRepo);
        var result = await service.LoginAsync(new LoginRequest { UsernameOrEmail = "alice@test.com", Password = "pass123" }, IP);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("access-token");
        result.RefreshToken.Should().Be("refresh-token");
        result.Role.Should().Be(RoleCodes.SocietyAdmin);
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsAuthenticationException()
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByUsernameOrEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var service = Build(userRepo);
        var act = () => service.LoginAsync(new LoginRequest { UsernameOrEmail = "ghost@test.com", Password = "x" }, IP);

        await act.Should().ThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsAuthenticationException()
    {
        var hasher = new PasswordHasher();
        var user = ActiveUser(hasher.Hash("pass123"));
        var userRepo = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByUsernameOrEmailAsync("alice@test.com")).ReturnsAsync(user);

        var service = Build(userRepo, hasher: hasher);
        var act = () => service.LoginAsync(new LoginRequest { UsernameOrEmail = "alice@test.com", Password = "wrong" }, IP);

        await act.Should().ThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsAuthenticationException()
    {
        var hasher = new PasswordHasher();
        var user = ActiveUser(hasher.Hash("pass")); user.IsActive = false;
        var userRepo = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByUsernameOrEmailAsync("alice@test.com")).ReturnsAsync(user);

        var service = Build(userRepo, hasher: hasher);
        var act = () => service.LoginAsync(new LoginRequest { UsernameOrEmail = "alice@test.com", Password = "pass" }, IP);

        await act.Should().ThrowAsync<AuthenticationException>().WithMessage("*inactive*");
    }

    [Fact]
    public async Task LoginAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = Build();
        var act = () => service.LoginAsync(null!, IP);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LoginAsync_UserWithNoRole_ThrowsAuthenticationException()
    {
        var hasher = new PasswordHasher();
        var user = ActiveUser(hasher.Hash("pass")); user.Role = null!;
        var userRepo = new Mock<IUserRepository>();

        userRepo.Setup(x => x.GetByUsernameOrEmailAsync("alice@test.com")).ReturnsAsync(user);

        var service = Build(userRepo, hasher: hasher);
        var act = () => service.LoginAsync(new LoginRequest { UsernameOrEmail = "alice@test.com", Password = "pass" }, IP);

        await act.Should().ThrowAsync<AuthenticationException>();
    }

    // ── RegisterAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = Build();
        var act = () => service.RegisterAsync(null!, IP);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsDuplicateException()
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByUsernameOrEmailAsync("alice@test.com")).ReturnsAsync(ActiveUser());

        var service = Build(userRepo, db: InMemoryDb());
        var req = new RegisterRequest { Email = "alice@test.com", Password = "pass", Name = "Alice", SocietyName = "Test" };

        var act = () => service.RegisterAsync(req, IP);
        await act.Should().ThrowAsync<DuplicateException>().WithMessage("*email*");
    }

    // ── Password reset ────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestPasswordResetAsync_UnknownEmail_ReturnsGenericMessageWithoutEmail()
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        var emailService = new Mock<IEmailService>();

        var service = Build(userRepo, emailService: emailService);
        var result = await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "nobody@test.com" });

        result.Message.Should().Contain("If an account exists");
        emailService.Verify(
            x => x.SendPasswordResetEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_ActiveUser_SendsEmailAndStoresToken()
    {
        var user = ActiveUser();
        var userRepo = new Mock<IUserRepository>();
        var emailService = new Mock<IEmailService>();

        userRepo.Setup(x => x.GetByEmailAsync(user.Email!)).ReturnsAsync(user);
        userRepo.Setup(x => x.SetPasswordResetTokenAsync(user.Id, It.IsAny<string>(), It.IsAny<DateTime>()))
            .Returns(Task.CompletedTask);
        emailService.Setup(x => x.SendPasswordResetEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = Build(userRepo, emailService: emailService);
        var result = await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = user.Email! });

        result.Message.Should().Contain("If an account exists");
        userRepo.Verify(x => x.SetPasswordResetTokenAsync(user.Id, It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
        emailService.Verify(
            x => x.SendPasswordResetEmailAsync(
                user.Email!, user.Name, It.Is<string>(l => l.Contains("/reset-password?token=")), true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResetPasswordWithTokenAsync_InvalidToken_ThrowsValidationException()
    {
        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByPasswordResetTokenHashAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var service = Build(userRepo);
        var act = () => service.ResetPasswordWithTokenAsync(
            new ResetPasswordRequest { Token = "bad", NewPassword = "NewPass1", ConfirmPassword = "NewPass1" }, IP);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ResetPasswordWithTokenAsync_ExpiredToken_ThrowsValidationException()
    {
        var user = ActiveUser();
        user.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(-5);

        var userRepo = new Mock<IUserRepository>();
        userRepo.Setup(x => x.GetByPasswordResetTokenHashAsync(It.IsAny<string>())).ReturnsAsync(user);

        var service = Build(userRepo);
        var act = () => service.ResetPasswordWithTokenAsync(
            new ResetPasswordRequest { Token = "tok", NewPassword = "NewPass1", ConfirmPassword = "NewPass1" }, IP);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("token");
        ex.Which.Errors["token"].Should().ContainMatch("*expired*");
    }

    [Fact]
    public async Task ResetPasswordWithTokenAsync_ValidToken_ResetsPasswordAndReturnsToken()
    {
        var user = ActiveUser();
        user.PasswordResetExpiresAt = DateTime.UtcNow.AddHours(1);

        var userRepo = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var hasher = new PasswordHasher();

        userRepo.Setup(x => x.GetByPasswordResetTokenHashAsync(It.IsAny<string>())).ReturnsAsync(user);
        userRepo.Setup(x => x.SetPasswordAndClearResetTokenAsync(user.Id, It.IsAny<string>())).Returns(Task.CompletedTask);
        tokenService.Setup(x => x.GenerateAccessToken(It.IsAny<TokenClaims>(), out It.Ref<DateTime>.IsAny))
            .Returns("reset-access-token");

        var service = Build(userRepo, hasher: hasher, tokenService: tokenService);
        var result = await service.ResetPasswordWithTokenAsync(
            new ResetPasswordRequest { Token = "valid", NewPassword = "NewPass1", ConfirmPassword = "NewPass1" }, IP);

        result.AccessToken.Should().Be("reset-access-token");
        userRepo.Verify(x => x.SetPasswordAndClearResetTokenAsync(user.Id, It.IsAny<string>()), Times.Once);
    }
}
