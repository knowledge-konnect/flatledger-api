using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using SocietyLedger.Api.Authorization;
using SocietyLedger.Api.BackgroundServices;
using SocietyLedger.Api.Endpoints;
using SocietyLedger.Api.Endpoints.Admin;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Api.Middlewares;
using SocietyLedger.Infrastructure.Persistence.Contexts;
using SocietyLedger.Shared;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

// Render.com injects a PORT env var at runtime; bind to it so the health
// check and reverse-proxy can reach the process on the correct port.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
    builder.WebHost.UseUrls($"http://+:{renderPort}");

var logTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{CorrelationId}] {Message}{NewLine}{Exception}";

builder.Host.UseSerilog((ctx, lc) =>
{
    var minLevel = ctx.HostingEnvironment.IsProduction() ? LogEventLevel.Warning : LogEventLevel.Information;
    lc
        .MinimumLevel.Is(minLevel)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName();

    if (ctx.HostingEnvironment.IsProduction())
    {
        // Render.com has an ephemeral filesystem, so log to stdout only.
        // Forward logs to a drain (Datadog, Papertrail, Logtail, etc.) via the Render dashboard.
        lc.WriteTo.Async(a => a.Console(outputTemplate: logTemplate));
    }
    else
    {
        lc.WriteTo.Async(a => a.Console(outputTemplate: logTemplate))
          .WriteTo.Async(a => a.File(
              "Logs/SocietyLedger-.txt",
              rollingInterval: RollingInterval.Day,
              retainedFileCountLimit: 14,
              fileSizeLimitBytes: 50_000_000,
              rollOnFileSizeLimit: true,
              outputTemplate: logTemplate));
    }
});

Log.Information("Starting SocietyLedger API...");

// CORS: AllowAnyOrigin() is forbidden when the frontend sends credentials
// (withCredentials: true / httpOnly cookie). Origins are loaded from config.
var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for httpOnly cookie support
    });
});

// Health check wired to EF Core — reports unhealthy if the DB is unreachable.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SocietyLedger API",
        Version = "v1"
    });

    // Enables the "Authorize" button in Swagger UI for JWT bearer tokens.
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer' followed by your token.\nExample: \"Bearer abc123\""
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});


// API Versioning — URL segment strategy (e.g. /api/v1/...).
// ReportApiVersions adds the api-supported-versions header to every response.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(ApiConstants.API_VERSION_1_0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Rate limiting: per-user (authenticated) or per-IP (anonymous), 100 req/min globally.
// Auth endpoints use a stricter 5 req/min policy for brute-force protection.
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";
        
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    // Applied explicitly via .RequireRateLimiting("AuthPolicy") on /login and /register.
    options.AddPolicy("AuthPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        var errorResponse = ErrorResponse.Create(
            "RATE_LIMIT_EXCEEDED",
            "Too many requests. Please try again later.",
            context.HttpContext.TraceIdentifier
        );
        await context.HttpContext.Response.WriteAsJsonAsync(errorResponse, token);
    };
});

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var key = jwtSettings["Key"];
if (string.IsNullOrWhiteSpace(key))
    throw new InvalidOperationException("JwtSettings:Key is missing or empty. Set the JwtSettings__Key environment variable.");
if (System.Text.Encoding.UTF8.GetByteCount(key) < 32)
    throw new InvalidOperationException("JwtSettings:Key must be at least 32 bytes (256 bits) for HMAC-SHA256. Use a longer key.");
var issuer = jwtSettings["Issuer"];
var audience = jwtSettings["Audience"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ActiveSubscription", policy =>
        policy.Requirements.Add(new SubscriptionRequirement()));

    // SuperAdmin policy grants access to all /api/admin/* endpoints.
    // Only JWTs issued by AdminAuthService carry the role:super_admin claim.
    options.AddPolicy("SuperAdmin", policy =>
        policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "super_admin"));
});

builder.Services.AddScoped<IAuthorizationHandler, SubscriptionAuthorizationHandler>();

// Application and infrastructure services
builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddSharedServices();

// Background services for monthly billing and trial expiration.
// Hangfire was removed in favour of these lightweight hosted services for the MVP.
builder.Services.AddHostedService<MonthlyBillGenerationService>();
builder.Services.AddHostedService<TrialExpirationService>();

// Trust Render's SSL-terminating load balancer so that Request.Scheme is "https"
// and X-Forwarded-For is populated correctly. Clearing known networks/proxies
// ensures Render's dynamic proxy IPs are always trusted.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Clear known-network/known-proxy defaults so Render's proxy IPs are trusted.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Middleware pipeline
// Must be first — corrects Request.Scheme / RemoteIpAddress before any other middleware reads them.
app.UseForwardedHeaders();

// Swagger is only served in development to avoid leaking the API surface.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SocietyLedger API V1");
        c.RoutePrefix = "";
    });
}

// SSL is terminated at the Render load balancer, so HTTPS redirect is only needed locally.
if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// Minimal security headers — tightened for an API-only surface (no browser assets served).
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options", "DENY");
    ctx.Response.Headers.Append("Referrer-Policy", "no-referrer");
    ctx.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    if (!ctx.Request.Path.StartsWithSegments("/swagger"))
    {
        ctx.Response.Headers.Append("Content-Security-Policy", "default-src 'none'");
    }
    await next();
});

// Correlation ID middleware — pushes CorrelationId into Serilog LogContext for every request.
app.UseMiddleware<SocietyLedger.Api.CorrelationIdMiddleware>();

app.UseCors("DefaultCorsPolicy");

// Serilog structured request logging: one log line per request with Method, Path, StatusCode, Elapsed, UserId.
// Health-check and Swagger probes are suppressed at Verbose level to reduce noise.
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0}ms";
    options.GetLevel = (ctx, elapsed, ex) =>
    {
        // Suppress health-check and Swagger probes — Verbose is always below the configured minimum.
        if (ctx.Request.Path.StartsWithSegments("/health") ||
            ctx.Request.Path.StartsWithSegments("/swagger"))
            return LogEventLevel.Verbose;
        return ex != null || ctx.Response.StatusCode >= 500 ? LogEventLevel.Error :
               ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning :
               elapsed > 1000 ? LogEventLevel.Warning :
               LogEventLevel.Information;
    };
    options.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
    {
        diagCtx.Set("UserId", httpCtx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous");
        diagCtx.Set("CorrelationId", httpCtx.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? httpCtx.TraceIdentifier);
    };
});

app.UseMiddleware<ExceptionMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapHealthChecks("/health");

// API Version Set & Endpoints
var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(ApiConstants.API_VERSION_1_0))
    .ReportApiVersions()
    .Build();

app.MapGroup(ApiRoutes.AUTH)
   .MapAuthRoutes(RouteGroupNames.AUTHENTICATION, versionSet);

app.MapGroup(ApiRoutes.FLATS)
   .MapFlatRoutes(RouteGroupNames.FLAT, versionSet);

app.MapGroup(ApiRoutes.USERS)
   .MapUserRoutes(RouteGroupNames.USER, versionSet);

app.MapGroup(ApiRoutes.SUBSCRIPTIONS)
   .MapSubscriptionRoutes(RouteGroupNames.SUBSCRIPTION, versionSet);

app.MapGroup(ApiRoutes.INVOICES)
   .MapInvoiceRoutes(RouteGroupNames.INVOICE, versionSet);

app.MapGroup(ApiRoutes.PLANS)
   .MapPlanRoutes(RouteGroupNames.PLAN, versionSet);

app.MapGroup(ApiRoutes.PAYMENTS)
   .MapPaymentRoutes(RouteGroupNames.PAYMENT, versionSet);

app.MapGroup(ApiRoutes.MAINTENANCE_PAYMENTS)
   .MapMaintenancePaymentRoutes(RouteGroupNames.MAINTENANCE_PAYMENT, versionSet);

app.MapGroup(ApiRoutes.PAYMENT_MODES)
   .MapPaymentModeRoutes(RouteGroupNames.PAYMENT_MODE, versionSet);

app.MapGroup(ApiRoutes.EXPENSES)
   .MapExpenseRoutes(RouteGroupNames.EXPENSES, versionSet);

app.MapGroup(ApiRoutes.OPENING_BALANCE)
   .MapOpeningBalanceRoutes(RouteGroupNames.OPENING_BALANCE, versionSet);

app.MapGroup(ApiRoutes.BILLING)
   .MapBillingRoutes(RouteGroupNames.BILLING, versionSet);

app.MapGroup(ApiRoutes.SOCIETIES)
   .MapSocietyRoutes(RouteGroupNames.SOCIETY, versionSet);

app.MapGroup(ApiRoutes.NOTIFICATIONS)
   .MapNotificationRoutes(RouteGroupNames.NOTIFICATION, versionSet);

app.MapGroup(ApiRoutes.DASHBOARD)
   .MapDashboardRoutes(RouteGroupNames.DASHBOARD, versionSet);

// SaaS Admin module routes — all require the SuperAdmin policy.
app.MapGroup(ApiRoutes.ADMIN_AUTH)
   .MapAdminAuthRoutes(RouteGroupNames.ADMIN_AUTH, versionSet);

app.MapGroup(ApiRoutes.ADMIN_PLANS)
    .MapAdminPlanRoutes(RouteGroupNames.ADMIN_PLANS, versionSet);

app.MapGroup(ApiRoutes.ADMIN_SOCIETIES)
    .MapAdminSocietyRoutes(RouteGroupNames.ADMIN_SOCIETIES, versionSet);

app.MapGroup(ApiRoutes.ADMIN_USERS)
    .MapAdminUserRoutes(RouteGroupNames.ADMIN_USERS, versionSet);

app.MapGroup(ApiRoutes.ADMIN_SUBSCRIPTIONS)
    .MapAdminSubscriptionRoutes(RouteGroupNames.ADMIN_SUBSCRIPTIONS, versionSet);

app.MapGroup(ApiRoutes.ADMIN_PAYMENTS)
    .MapAdminPaymentRoutes(RouteGroupNames.ADMIN_PAYMENTS, versionSet);

app.MapGroup(ApiRoutes.ADMIN_BILLS)
    .MapAdminBillRoutes(RouteGroupNames.ADMIN_BILLS, versionSet);

app.MapGroup(ApiRoutes.ADMIN_INVOICES)
    .MapAdminInvoiceRoutes(RouteGroupNames.ADMIN_INVOICES, versionSet);

app.MapGroup(ApiRoutes.ADMIN_SETTINGS)
    .MapAdminPlatformSettingRoutes(RouteGroupNames.ADMIN_SETTINGS, versionSet);

app.MapGroup(ApiRoutes.REPORTS)
   .MapReportRoutes(RouteGroupNames.REPORTS, versionSet);

// Proactively warm up the Supabase connection pool on startup.
// Free-tier Supabase instances can take 60-90s to resume from idle; retrying here
// prevents the first real request from timing out after a cold start.
// A dedicated 120s timeout is used only for this warmup ping — normal queries use 30s.
const int maxWarmupAttempts = 5;
for (int attempt = 1; attempt <= maxWarmupAttempts; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("SET statement_timeout = '120s'; SELECT 1");
        Log.Information("Database warmup successful on attempt {Attempt}.", attempt);
        break;
    }
    catch (Exception ex)
    {
        Log.Warning("Database warmup attempt {Attempt}/{Max} failed: {Message}", attempt, maxWarmupAttempts, ex.Message);
        if (attempt < maxWarmupAttempts)
            await Task.Delay(TimeSpan.FromSeconds(30));
        else
            Log.Warning("Database warmup exhausted — first requests may be slow.");
    }
}

app.Run();
