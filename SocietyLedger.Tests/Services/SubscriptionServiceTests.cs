using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SocietyLedger.Application.DTOs.Subscription;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Services;
using Xunit;

namespace SocietyLedger.Tests.Services;

public class SubscriptionServiceTests
{
    private static AppDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static SubscriptionService BuildService(
        Mock<ISubscriptionRepository>? subscriptionRepo = null,
        Mock<IPlanRepository>? planRepo = null,
        Mock<IInvoiceRepository>? invoiceRepo = null,
        Mock<ISubscriptionEventRepository>? eventRepo = null,
        Mock<ISocietyRepository>? societyRepo = null,
        AppDbContext? db = null,
        IMemoryCache? cache = null)
    {
        return new SubscriptionService(
            subscriptionRepo?.Object ?? new Mock<ISubscriptionRepository>().Object,
            planRepo?.Object ?? new Mock<IPlanRepository>().Object,
            invoiceRepo?.Object ?? new Mock<IInvoiceRepository>().Object,
            eventRepo?.Object ?? new Mock<ISubscriptionEventRepository>().Object,
            societyRepo?.Object ?? new Mock<ISocietyRepository>().Object,
            db ?? BuildInMemoryDb(),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<SubscriptionService>.Instance);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetSubscriptionStatusAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSubscriptionStatusAsync_UserNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync((long?)null);
        var svc = BuildService(societyRepo: societyRepo);

        // Act
        var act = () => svc.GetSubscriptionStatusAsync(1);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_NoSubscription_ReturnsNoneNotAllowed()
    {
        // Arrange
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync((Subscription?)null);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var result = await svc.GetSubscriptionStatusAsync(1);

        // Assert
        result.Status.Should().Be("none");
        result.AccessAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_TrialActive_ReturnsAccessAllowedWithDaysRemaining()
    {
        // Arrange
        var trialEnd = DateTime.UtcNow.AddDays(5);
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Trial,
            TrialEnd = trialEnd,
            SubscribedAmount = 0,
            Currency = "INR"
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var result = await svc.GetSubscriptionStatusAsync(1);

        // Assert
        result.Status.Should().Be(SubscriptionStatusCodes.Trial);
        result.AccessAllowed.Should().BeTrue();
        result.TrialDaysRemaining.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_TrialExpired_ReturnsNotAllowed()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Trial,
            TrialEnd = DateTime.UtcNow.AddDays(-2),
            SubscribedAmount = 0,
            Currency = "INR"
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var result = await svc.GetSubscriptionStatusAsync(1);

        // Assert
        result.Status.Should().Be(SubscriptionStatusCodes.Trial);
        result.AccessAllowed.Should().BeFalse();
        result.TrialDaysRemaining.Should().BeNull();
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_ActiveSubscription_ReturnsAllowed()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
            SubscribedAmount = 499,
            Currency = "INR"
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var result = await svc.GetSubscriptionStatusAsync(1);

        // Assert
        result.Status.Should().Be(SubscriptionStatusCodes.Active);
        result.AccessAllowed.Should().BeTrue();
        result.MonthlyAmount.Should().Be(499);
        result.Currency.Should().Be("INR");
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_CancelledPeriodStillActive_ReturnsAllowed()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Cancelled,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            SubscribedAmount = 499,
            Currency = "INR"
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var result = await svc.GetSubscriptionStatusAsync(1);

        // Assert
        result.Status.Should().Be(SubscriptionStatusCodes.Cancelled);
        result.AccessAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_CancelledPeriodExpired_ReturnsNotAllowed()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Cancelled,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-5),
            SubscribedAmount = 499,
            Currency = "INR"
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var result = await svc.GetSubscriptionStatusAsync(1);

        // Assert
        result.Status.Should().Be(SubscriptionStatusCodes.Cancelled);
        result.AccessAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetSubscriptionStatusAsync_ExpiredSubscription_ReturnsNotAllowed()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Expired,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1),
            SubscribedAmount = 499,
            Currency = "INR"
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var result = await svc.GetSubscriptionStatusAsync(1);

        // Assert
        result.Status.Should().Be(SubscriptionStatusCodes.Expired);
        result.AccessAllowed.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CreateTrialSubscriptionAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTrialSubscriptionAsync_UserHasNoSociety_ReturnsWithoutCreating()
    {
        // Arrange
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync((long?)null);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        await svc.CreateTrialSubscriptionAsync(1);

        // Assert — subscription was never created
        subscriptionRepo.Verify(r => r.CreateAsync(It.IsAny<Subscription>()), Times.Never);
    }

    [Fact]
    public async Task CreateTrialSubscriptionAsync_ExistingSubscription_ReturnsWithoutCreating()
    {
        // Arrange
        var existing = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Trial
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(existing);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        await svc.CreateTrialSubscriptionAsync(1);

        // Assert — no new subscription created
        subscriptionRepo.Verify(r => r.CreateAsync(It.IsAny<Subscription>()), Times.Never);
    }

    [Fact]
    public async Task CreateTrialSubscriptionAsync_NoActivePlans_ThrowsNotFoundException()
    {
        // Arrange
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync((Subscription?)null);

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetActivePlansAsync()).ReturnsAsync(Enumerable.Empty<Plan>());

        var svc = BuildService(subscriptionRepo: subscriptionRepo, planRepo: planRepo, societyRepo: societyRepo);

        // Act
        var act = () => svc.CreateTrialSubscriptionAsync(1);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Plan*");
    }

    [Fact]
    public async Task CreateTrialSubscriptionAsync_NoExistingSubscription_CreatesTrialAndEvent()
    {
        // Arrange
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Starter",
            Currency = "INR",
            DurationMonths = 1,
            MaxFlats = 50,
            IsActive = true,
            PlanGroup = "monthly"
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync((Subscription?)null);

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetActivePlansAsync()).ReturnsAsync(new[] { plan });

        var eventRepo = new Mock<ISubscriptionEventRepository>();

        var svc = BuildService(
            subscriptionRepo: subscriptionRepo,
            planRepo: planRepo,
            eventRepo: eventRepo,
            societyRepo: societyRepo);

        // Act
        await svc.CreateTrialSubscriptionAsync(1);

        // Assert
        subscriptionRepo.Verify(r => r.CreateAsync(It.Is<Subscription>(s =>
            s.Status == SubscriptionStatusCodes.Trial &&
            s.SocietyId == 10 &&
            s.UserId == 1 &&
            s.SubscribedAmount == 0)), Times.Once);

        eventRepo.Verify(r => r.CreateAsync(It.Is<SubscriptionEvent>(e =>
            e.EventType == "trial_started" &&
            e.SocietyId == 10)), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CancelSubscriptionAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelSubscriptionAsync_NoSubscription_ThrowsNotFoundException()
    {
        // Arrange
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync((Subscription?)null);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var act = () => svc.CancelSubscriptionAsync(1, new CancelSubscriptionRequest());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Subscription*");
    }

    [Fact]
    public async Task CancelSubscriptionAsync_AlreadyExpired_ThrowsConflictException()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Expired
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var act = () => svc.CancelSubscriptionAsync(1, new CancelSubscriptionRequest());

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*active or trial*");
    }

    [Fact]
    public async Task CancelSubscriptionAsync_AlreadyCancelled_ThrowsConflictException()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Cancelled
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var act = () => svc.CancelSubscriptionAsync(1, new CancelSubscriptionRequest());

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*active or trial*");
    }

    [Fact]
    public async Task CancelSubscriptionAsync_ActiveSubscription_SetsStatusCancelledAndCreatesEvent()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SocietyId = 10,
            Status = SubscriptionStatusCodes.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(15)
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var eventRepo = new Mock<ISubscriptionEventRepository>();

        var svc = BuildService(
            subscriptionRepo: subscriptionRepo,
            eventRepo: eventRepo,
            societyRepo: societyRepo);

        // Act
        await svc.CancelSubscriptionAsync(1, new CancelSubscriptionRequest { Reason = "too expensive" });

        // Assert
        subscriptionRepo.Verify(r => r.UpdateAsync(It.Is<Subscription>(s =>
            s.Status == SubscriptionStatusCodes.Cancelled &&
            s.CancelledAt != null)), Times.Once);

        eventRepo.Verify(r => r.CreateAsync(It.Is<SubscriptionEvent>(e =>
            e.EventType == "cancelled" &&
            e.OldStatus == SubscriptionStatusCodes.Active &&
            e.NewStatus == SubscriptionStatusCodes.Cancelled)), Times.Once);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_TrialSubscription_SetsStatusCancelled()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SocietyId = 10,
            Status = SubscriptionStatusCodes.Trial,
            TrialEnd = DateTime.UtcNow.AddDays(10)
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var eventRepo = new Mock<ISubscriptionEventRepository>();

        var svc = BuildService(
            subscriptionRepo: subscriptionRepo,
            eventRepo: eventRepo,
            societyRepo: societyRepo);

        // Act
        await svc.CancelSubscriptionAsync(1, new CancelSubscriptionRequest());

        // Assert
        subscriptionRepo.Verify(r => r.UpdateAsync(It.Is<Subscription>(s =>
            s.Status == SubscriptionStatusCodes.Cancelled)), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SubscribeAsync — pre-transaction guards
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeAsync_PlanNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Plan?)null);

        var svc = BuildService(planRepo: planRepo);

        // Act
        var act = () => svc.SubscribeAsync(1, new SubscribeRequest { PlanId = Guid.NewGuid() });

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Plan*");
    }

    [Fact]
    public async Task SubscribeAsync_PlanInactive_ThrowsConflictException()
    {
        // Arrange
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Basic",
            IsActive = false,
            Currency = "INR",
            DurationMonths = 1,
            MaxFlats = 50,
            PlanGroup = "monthly"
        };

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);

        var svc = BuildService(planRepo: planRepo);

        // Act
        var act = () => svc.SubscribeAsync(1, new SubscribeRequest { PlanId = plan.Id });

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*not currently active*");
    }

    [Fact]
    public async Task SubscribeAsync_SocietyNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Basic",
            IsActive = true,
            Currency = "INR",
            DurationMonths = 1,
            MaxFlats = 50,
            PlanGroup = "monthly"
        };

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync((long?)null);

        var svc = BuildService(planRepo: planRepo, societyRepo: societyRepo);

        // Act
        var act = () => svc.SubscribeAsync(1, new SubscribeRequest { PlanId = plan.Id });

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*User*");
    }

    [Fact]
    public async Task SubscribeAsync_TooManyFlats_ThrowsConflictException()
    {
        // Arrange
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Name = "Starter",
            IsActive = true,
            Currency = "INR",
            Price = 499,
            DurationMonths = 1,
            MaxFlats = 10,
            PlanGroup = "monthly"
        };

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetByIdAsync(plan.Id)).ReturnsAsync(plan);

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(r => r.CountActiveFlatsBySocietyAsync(10)).ReturnsAsync(15); // exceeds limit

        var svc = BuildService(planRepo: planRepo, societyRepo: societyRepo);

        // Act
        var act = () => svc.SubscribeAsync(1, new SubscribeRequest { PlanId = plan.Id });

        // Assert
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*10 flats*15 active*");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ValidateSubscriptionAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateSubscriptionAsync_NoSubscription_ReturnsFalseWithMessage()
    {
        // Arrange
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync((Subscription?)null);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (isValid, message) = await svc.ValidateSubscriptionAsync(1);

        // Assert
        isValid.Should().BeFalse();
        message.Should().Contain("No active subscription");
    }

    [Fact]
    public async Task ValidateSubscriptionAsync_ActiveSubscriptionPeriodValid_ReturnsTrue()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(15)
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (isValid, _) = await svc.ValidateSubscriptionAsync(1);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSubscriptionAsync_ActivePeriodLapsed_UpdatesStatusToExpiredReturnsFalse()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SocietyId = 10,
            Status = SubscriptionStatusCodes.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1) // period already ended
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (isValid, message) = await svc.ValidateSubscriptionAsync(1);

        // Assert
        isValid.Should().BeFalse();
        message.Should().Contain("expired");
        subscription.Status.Should().Be(SubscriptionStatusCodes.Expired);

        subscriptionRepo.Verify(r => r.UpdateAsync(It.Is<Subscription>(s =>
            s.Status == SubscriptionStatusCodes.Expired)), Times.Once);
    }

    [Fact]
    public async Task ValidateSubscriptionAsync_TrialStillActive_ReturnsTrue()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Trial,
            TrialEnd = DateTime.UtcNow.AddDays(3)
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (isValid, _) = await svc.ValidateSubscriptionAsync(1);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSubscriptionAsync_TrialExpired_ReturnsFalse()
    {
        // Arrange
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Trial,
            TrialEnd = DateTime.UtcNow.AddDays(-1)
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (isValid, message) = await svc.ValidateSubscriptionAsync(1);

        // Assert
        isValid.Should().BeFalse();
        message.Should().Contain("expired");
    }

    [Fact]
    public async Task ValidateSubscriptionAsync_CancelledPeriodFuture_ReturnsTrue()
    {
        // Arrange — cancelled but paid period still running
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Cancelled,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(5)
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (isValid, _) = await svc.ValidateSubscriptionAsync(1);

        // Assert
        isValid.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // CanAddFlatsAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CanAddFlatsAsync_SubscriptionInvalid_ReturnsFalse()
    {
        // Arrange — no subscription
        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync((Subscription?)null);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (allowed, _) = await svc.CanAddFlatsAsync(1, 1);

        // Assert
        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task CanAddFlatsAsync_WouldExceedPlanLimit_ReturnsFalse()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var plan = new Plan
        {
            Id = planId,
            MaxFlats = 10,
            IsActive = true,
            Currency = "INR",
            DurationMonths = 1,
            PlanGroup = "monthly",
            Name = "Basic"
        };
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
            PlanId = planId,
            Plan = plan // navigation property pre-loaded
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(r => r.CountActiveFlatsBySocietyAsync(10)).ReturnsAsync(9); // 9 + 2 > 10

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (allowed, message) = await svc.CanAddFlatsAsync(1, 2);

        // Assert
        allowed.Should().BeFalse();
        message.Should().Contain("10");
    }

    [Fact]
    public async Task CanAddFlatsAsync_WithinLimit_ReturnsTrue()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var plan = new Plan
        {
            Id = planId,
            MaxFlats = 50,
            IsActive = true,
            Currency = "INR",
            DurationMonths = 1,
            PlanGroup = "monthly",
            Name = "Pro"
        };
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            Status = SubscriptionStatusCodes.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
            PlanId = planId,
            Plan = plan
        };

        var societyRepo = new Mock<ISocietyRepository>();
        societyRepo.Setup(r => r.GetSocietyIdByUserIdAsync(1)).ReturnsAsync(10L);
        societyRepo.Setup(r => r.CountActiveFlatsBySocietyAsync(10)).ReturnsAsync(10);

        var subscriptionRepo = new Mock<ISubscriptionRepository>();
        subscriptionRepo.Setup(r => r.GetBySocietyIdAsync(10)).ReturnsAsync(subscription);

        var svc = BuildService(subscriptionRepo: subscriptionRepo, societyRepo: societyRepo);

        // Act
        var (allowed, _) = await svc.CanAddFlatsAsync(1, 5);

        // Assert
        allowed.Should().BeTrue();
    }
}
