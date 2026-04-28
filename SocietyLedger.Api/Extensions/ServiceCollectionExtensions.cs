using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SocietyLedger.Api.Filters;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Application.Interfaces.Services.Admin;
using SocietyLedger.Application.Validators.Auth;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Infrastructure.Persistence.Repositories;using SocietyLedger.Infrastructure.Services;
using SocietyLedger.Infrastructure.Services.Admin;
using SocietyLedger.Infrastructure.Services.Common;
using SocietyLedger.Shared.Jwt;
namespace SocietyLedger.Api.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers application-level use-case services and FluentValidation validators.
        /// </summary>
        public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

            // Add application-level services (use cases)
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<IFlatService, FlatService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<ISubscriptionService, SubscriptionService>();
            services.AddScoped<IInvoiceService, InvoiceService>();
            services.AddScoped<IPlanService, PlanService>();
            services.AddScoped<IRazorpayPaymentService, RazorpayPaymentService>();
            services.AddScoped<IMaintenancePaymentService, MaintenancePaymentService>();
            services.AddScoped<IExpenseService, ExpenseService>();
            services.AddScoped<IOpeningBalanceService, OpeningBalanceService>();
            services.AddScoped<IBillingService, BillingService>();
            services.AddScoped<IMaintenanceConfigService, MaintenanceConfigService>();
            services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();

            // SaaS Admin module
            services.AddScoped<IAdminAuthService, AdminAuthService>();
            services.AddScoped<IAdminPlanService, AdminPlanService>();
            services.AddScoped<IAdminSocietyService, AdminSocietyService>();
            services.AddScoped<IAdminUserService, AdminUserService>();
            services.AddScoped<IAdminSubscriptionService, AdminSubscriptionService>();
            services.AddScoped<IAdminPaymentService, AdminPaymentService>();
            services.AddScoped<IAdminBillService, AdminBillService>();
            services.AddScoped<IAdminInvoiceService, AdminInvoiceService>();
            services.AddScoped<IAdminPlatformSettingService, AdminPlatformSettingService>();

            return services;
        }

        /// <summary>
        /// Registers infrastructure services: EF Core (Npgsql), Dapper, all repositories,
        /// security helpers (PasswordHasher, TokenService), and endpoint filters.
        /// Uses a 120-second command timeout to handle Supabase free-tier cold starts.
        /// pgBouncer transaction mode (port 6543) is assumed — savepoints and retries are disabled.
        /// </summary>
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();

            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var dataSource = new NpgsqlDataSourceBuilder(connectionString)
                .Build();

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(dataSource, npgsql =>
                {
                    // 30s default timeout for normal queries. Cold-start handling is done
                    // in the startup warmup loop (5 retries × 30s delay), not by inflating
                    // the command timeout for every query.
                    npgsql.CommandTimeout(30);
                }));

            // Common infrastructure helpers
            services.AddScoped<IUserContext, UserContext>();

            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<IFlatRepository, FlatRepository>();
            services.AddScoped<ISocietyRepository, SocietyRepository>();
            services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
            services.AddScoped<IInvoiceRepository, InvoiceRepository>();
            services.AddScoped<IPlanRepository, PlanRepository>();
            services.AddScoped<ISubscriptionEventRepository, SubscriptionEventRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();
            services.AddScoped<IMaintenancePaymentRepository, MaintenancePaymentRepository>();
            services.AddScoped<IPaymentModeRepository, PaymentModeRepository>();
            services.AddScoped<IExpenseRepository, ExpenseRepository>();

            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IAdjustmentRepository, AdjustmentRepository>();
            services.AddScoped<IMaintenanceConfigRepository, MaintenanceConfigRepository>();
            services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
            services.AddScoped<IBillRepository, BillRepository>();

            services.AddSingleton<SocietyLedger.Infrastructure.Data.IDbConnectionFactory, SocietyLedger.Infrastructure.Data.DbConnectionFactory>();
            services.AddScoped<IDapperService, DapperService>();

            // Dashboard Services (Dapper-based)
            services.AddScoped<IDashboardRepository, DashboardRepository>();
            services.AddScoped<IDashboardService, DashboardService>();

            // Report Services (Dapper-based)
            services.AddScoped<IReportRepository, ReportRepository>();
            services.AddScoped<IReportExportService, ReportExportService>();
            services.AddScoped<IReportService, ReportService>();

            services.AddSingleton<PasswordHasher>();
            services.AddScoped<ITokenService, TokenService>();

            services.AddSingleton(typeof(FluentValidationFilter<>));
            services.AddSingleton<ViewerForbiddenFilter>();

            services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

            return services;
        }

        /// <summary>
        /// Registers shared cross-cutting services: logging and in-memory cache.
        /// </summary>
        public static IServiceCollection AddSharedServices(this IServiceCollection services)
        {
            services.AddLogging();
            services.AddMemoryCache();
            return services;
        }
    }
}
