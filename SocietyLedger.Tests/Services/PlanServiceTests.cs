using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Services;
using Xunit;

namespace SocietyLedger.Tests.Services;

public class PlanServiceTests
{
    private static PlanService BuildService(Mock<IPlanRepository>? planRepo = null)
    {
        return new PlanService(
            planRepo?.Object ?? new Mock<IPlanRepository>().Object,
            NullLogger<PlanService>.Instance);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetActivePlansAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActivePlansAsync_ReturnsAllActivePlansAsDtos()
    {
        // Arrange
        var plans = new[]
        {
            new Plan
            {
                Id = Guid.NewGuid(),
                Name = "Starter",
                Price = 199,
                Currency = "INR",
                IsActive = true,
                DurationMonths = 1,
                MaxFlats = 25,
                PlanGroup = "monthly",
                IsPopular = false,
                DisplayOrder = 1
            },
            new Plan
            {
                Id = Guid.NewGuid(),
                Name = "Pro",
                Price = 499,
                Currency = "INR",
                IsActive = true,
                DurationMonths = 3,
                MaxFlats = 100,
                PlanGroup = "quarterly",
                IsPopular = true,
                DisplayOrder = 2
            }
        };

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetActivePlansAsync()).ReturnsAsync(plans);

        var svc = BuildService(planRepo);

        // Act
        var result = (await svc.GetActivePlansAsync()).ToList();

        // Assert
        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Starter");
        result[0].Price.Should().Be(199);
        result[1].Name.Should().Be("Pro");
        result[1].IsPopular.Should().BeTrue();
    }

    [Fact]
    public async Task GetActivePlansAsync_NoPlans_ReturnsEmpty()
    {
        // Arrange
        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetActivePlansAsync()).ReturnsAsync(Enumerable.Empty<Plan>());

        var svc = BuildService(planRepo);

        // Act
        var result = await svc.GetActivePlansAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActivePlansAsync_MapsDtoFieldsCorrectly()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var created = new DateTime(2025, 1, 1);
        var plan = new Plan
        {
            Id = planId,
            Name = "Annual",
            Price = 1999,
            Currency = "INR",
            IsActive = true,
            CreatedAt = created,
            DurationMonths = 12,
            MaxFlats = 200,
            PlanGroup = "annual",
            IsPopular = true,
            Description = "Best value",
            DiscountPercentage = 20,
            DisplayOrder = 3
        };

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetActivePlansAsync()).ReturnsAsync(new[] { plan });

        var svc = BuildService(planRepo);

        // Act
        var result = (await svc.GetActivePlansAsync()).Single();

        // Assert
        result.Id.Should().Be(planId);
        result.Name.Should().Be("Annual");
        result.Price.Should().Be(1999);
        result.Currency.Should().Be("INR");
        result.DurationMonths.Should().Be(12);
        result.MaxFlats.Should().Be(200);
        result.PlanGroup.Should().Be("annual");
        result.IsPopular.Should().BeTrue();
        result.Description.Should().Be("Best value");
        result.DiscountPercentage.Should().Be(20);
        result.DisplayOrder.Should().Be(3);
        result.CreatedAt.Should().Be(created);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetPlanByIdAsync
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPlanByIdAsync_PlanNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Plan?)null);

        var svc = BuildService(planRepo);

        // Act
        var act = () => svc.GetPlanByIdAsync(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Plan*");
    }

    [Fact]
    public async Task GetPlanByIdAsync_PlanFound_ReturnsDtoWithAllFields()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var plan = new Plan
        {
            Id = planId,
            Name = "Pro",
            Price = 499,
            Currency = "INR",
            IsActive = true,
            DurationMonths = 3,
            MaxFlats = 100,
            PlanGroup = "quarterly",
            IsPopular = true,
            Description = "Great plan",
            DiscountPercentage = 10,
            DisplayOrder = 2
        };

        var planRepo = new Mock<IPlanRepository>();
        planRepo.Setup(r => r.GetByIdAsync(planId)).ReturnsAsync(plan);

        var svc = BuildService(planRepo);

        // Act
        var result = await svc.GetPlanByIdAsync(planId);

        // Assert
        result.Id.Should().Be(planId);
        result.Name.Should().Be("Pro");
        result.Price.Should().Be(499);
        result.MaxFlats.Should().Be(100);
        result.PlanGroup.Should().Be("quarterly");
    }
}
